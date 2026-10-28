using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Basil.Testing.Clients;

/// <summary>A client of the HTTP API: Bearer or anonymous, never following redirects.</summary>
public sealed class RestClient(Uri address, string domain, string? bearer = null) : IDisposable
{
	private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = address };

	/// <summary>Gets or sets the Bearer token the client sends.</summary>
	public string? Bearer { get; set; } = bearer;

	/// <summary>Sends a request to the <c>api.</c> host.</summary>
	/// <param name="method">The HTTP method.</param>
	/// <param name="path">The path and query.</param>
	/// <param name="body">A JSON body, or <see langword="null" />.</param>
	/// <param name="cancellationToken">A token that cancels the request.</param>
	public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null,
		CancellationToken cancellationToken = default)
	{
		using var request = new HttpRequestMessage(method, path);
		request.Headers.Host = $"api.{domain}";
		if (Bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Bearer);
		if (body is not null) request.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
		return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
	}

	/// <summary>Sends a multipart form with an optional file and text fields.</summary>
	/// <param name="method">The HTTP method.</param>
	/// <param name="path">The path.</param>
	/// <param name="file">The name of the file field, its file name and content, or <see langword="null" /> for none.</param>
	/// <param name="fields">The text fields.</param>
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
		using var request = new HttpRequestMessage(method, path) { Content = form };
		request.Headers.Host = $"api.{domain}";
		if (Bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Bearer);
		using var response = await _http.SendAsync(request);
		var text = await response.Content.ReadAsStringAsync();
		return ((int)response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
	}

	/// <summary>Sends a GET request to another sub-domain, such as the avatar host.</summary>
	/// <param name="label">The first label of the host name, such as <c>a</c>.</param>
	/// <param name="path">The path and query.</param>
	public async Task<HttpResponseMessage> GetFromAsync(string label, string path, string? ifNoneMatch = null)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, path);
		request.Headers.Host = $"{label}.{domain}";
		if (ifNoneMatch is not null) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
		return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
	}

	/// <summary>Sends a GET request.</summary>
	public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default)
	{
		return SendAsync(HttpMethod.Get, path, null, cancellationToken);
	}

	/// <summary>Opens a Server-Sent Events stream; the caller disposes the response to end it.</summary>
	/// <param name="path">The path of the live route.</param>
	/// <param name="lastEventId">The <c>Last-Event-ID</c> to send, or <see langword="null" />.</param>
	/// <returns>The response with its headers read, and a reader over its events when it is a stream.</returns>
	public async Task<(HttpResponseMessage Response, SseReader? Reader)> OpenStreamAsync(string path,
		string? lastEventId = null, CancellationToken cancellationToken = default)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, path);
		request.Headers.Host = $"api.{domain}";
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
		if (Bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Bearer);
		if (lastEventId is not null) request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
		var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		return (response, response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/event-stream"
			? new SseReader(await response.Content.ReadAsStreamAsync(cancellationToken))
			: null);
	}

	/// <summary>Sends a request and reads the envelope of the answer.</summary>
	public async Task<(int Status, JsonNode? Envelope)> CallAsync(HttpMethod method, string path, object? body = null)
	{
		using var response = await SendAsync(method, path, body);
		var text = await response.Content.ReadAsStringAsync();
		return ((int)response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
	}

	/// <summary>Signs in and keeps the token.</summary>
	/// <returns>The user id the token belongs to.</returns>
	public async Task<int> LoginAsync(string username, string password = "password")
	{
		var (status, envelope) = await CallAsync(HttpMethod.Post, "/v1/auth/login", new { username, password });
		if (status != 201) throw new InvalidOperationException($"Login failed with {status}.");
		Bearer = (string)envelope!["data"]!["token"]!;
		return (int)envelope["data"]!["userId"]!;
	}

	public void Dispose() => _http.Dispose();
}

/// <summary>Reads Server-Sent Events from a response stream, in order.</summary>
public sealed class SseReader(Stream stream)
{
	private readonly StreamReader _reader = new(stream);

	/// <summary>Gets the <c>id:</c> of the event read last, or <see langword="null" />.</summary>
	public string? LastId { get; private set; }

	/// <summary>Reads the next event, or <see langword="null" /> at the end of the stream; comments are skipped.</summary>
	public async Task<(string Event, string Data)?> ReadAsync(CancellationToken cancellationToken = default)
	{
		string eventName = "message", data = string.Empty;
		var any = false;
		// A stream that says nothing for this long is a failed test, not a slow one.
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
