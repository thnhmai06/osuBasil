using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Basil.Testing.Clients;

public sealed class RestClient(Uri address, string domain, string? bearer = null) : IDisposable
{
	private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
		{ BaseAddress = address };

	public string? Bearer { get; set; } = bearer;

	public void Dispose()
	{
		_http.Dispose();
	}

	public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null,
		CancellationToken ct = default)
	{
		using var request = new HttpRequestMessage(method, path);
		request.Headers.Host = $"api.{domain}";
		if (Bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Bearer);
		if (body is not null)
			request.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
		return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
	}

	public async Task<(int Status, JsonNode? Envelope)> FormAsync(HttpMethod method, string path,
		(string Field, string FileName, byte[] Content)? file = null, params (string Name, string Value)[] fields)
	{
		using var form = new MultipartFormDataContent();
		if (file is { } f)
		{
			var content = new ByteArrayContent(f.Content);
			content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
			form.Add(content, f.Field, f.FileName);
		}

		foreach (var (name, value) in fields) form.Add(new StringContent(value), name);
		using var request = new HttpRequestMessage(method, path);
		request.Content = form;
		request.Headers.Host = $"api.{domain}";
		if (Bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Bearer);
		using var response = await _http.SendAsync(request);
		var text = await response.Content.ReadAsStringAsync();
		return ((int)response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
	}

	public async Task<HttpResponseMessage> GetFromAsync(string label, string path, string? ifNoneMatch = null)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, path);
		request.Headers.Host = $"{label}.{domain}";
		if (ifNoneMatch is not null) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
		return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
	}

	public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default)
	{
		return SendAsync(HttpMethod.Get, path, null, ct);
	}

	public async Task<(HttpResponseMessage Response, SseReader? Reader)> OpenStreamAsync(string path,
		string? lastEventId = null, CancellationToken ct = default)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, path);
		request.Headers.Host = $"api.{domain}";
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
		if (Bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Bearer);
		if (lastEventId is not null) request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
		var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
		return (response, response.IsSuccessStatusCode &&
		                  response.Content.Headers.ContentType?.MediaType == "text/event-stream"
			? new SseReader(await response.Content.ReadAsStreamAsync(ct))
			: null);
	}

	public async Task<(int Status, JsonNode? Envelope)> CallAsync(HttpMethod method, string path, object? body = null)
	{
		using var response = await SendAsync(method, path, body);
		var text = await response.Content.ReadAsStringAsync();
		return ((int)response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
	}

	public async Task<int> LoginAsync(string username, string password = "password")
	{
		var (status, envelope) = await CallAsync(HttpMethod.Post, "/v1/auth/login", new { username, password });
		if (status != 201) throw new InvalidOperationException($"Login failed with {status}.");
		Bearer = (string)envelope!["data"]!["token"]!;
		return (int)envelope["data"]!["userId"]!;
	}
}

public sealed class SseReader(Stream stream)
{
	private readonly StreamReader _reader = new(stream);

	public string? LastId { get; private set; }

	public async Task<(string Event, string Data)?> ReadAsync(CancellationToken ct = default)
	{
		string eventName = "message", data = string.Empty;
		var any = false;
		// A stream that says nothing for this long is a failed test, not a slow one.
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(TimeSpan.FromSeconds(15));
		while (await _reader.ReadLineAsync(timeout.Token) is { } line)
		{
			if (line.Length == 0)
			{
				if (!any) continue;
				return (eventName, data);
			}

			if (line.StartsWith(':')) continue;
			any = true;
			if (line.StartsWith("event:", StringComparison.Ordinal)) eventName = line[6..].Trim();
			else if (line.StartsWith("data:", StringComparison.Ordinal)) data += line[5..].TrimStart();
			else if (line.StartsWith("id:", StringComparison.Ordinal)) LastId = line[3..].Trim();
		}

		return null;
	}
}