using System.Net.Http.Headers;

namespace Basil.Testing.Clients;

/// <summary>A minimal client of the HTTP API; Phase 3 extends it.</summary>
public sealed class RestClient(Uri address, string domain, string? bearer = null) : IDisposable
{
	private readonly HttpClient _http = new() { BaseAddress = address };

	public async Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, path);
		request.Headers.Host = $"api.{domain}";
		if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
		return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
	}

	public void Dispose() => _http.Dispose();
}

/// <summary>Reads Server-Sent Events from a response stream, in order.</summary>
public sealed class SseReader(Stream stream)
{
	private readonly StreamReader _reader = new(stream);

	/// <summary>Reads the next event, or <see langword="null" /> at the end of the stream.</summary>
	public async Task<(string Event, string Data)?> ReadAsync(CancellationToken cancellationToken = default)
	{
		string eventName = "message", data = string.Empty;
		while (await _reader.ReadLineAsync(cancellationToken) is { } line)
		{
			if (line.Length == 0) return (eventName, data);
			if (line.StartsWith("event:", StringComparison.Ordinal)) eventName = line[6..].Trim();
			else if (line.StartsWith("data:", StringComparison.Ordinal)) data += line[5..].TrimStart();
		}

		return null;
	}
}
