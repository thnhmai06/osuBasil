using System.Net;

namespace Basil.Tests.Shared.Clients;

/// <summary>
///     Where the server under test is reached: one connect address and port, and the domain whose subdomains name its
///     hosts.
/// </summary>
/// <param name="Connect">The address every request is sent to.</param>
/// <param name="Port">The port of the HTTPS front.</param>
/// <param name="Domain">The domain the hosts are named under, such as <c>basil.local</c>.</param>
public sealed record Endpoint(string Connect, int Port, string Domain)
{
	/// <summary>Gets the host name a subdomain stands for, such as <c>c.basil.local</c>.</summary>
	public string HostOf(string subdomain)
	{
		return $"{subdomain}.{Domain}";
	}

	/// <summary>Creates a request to the connect address that asks for the host of a subdomain.</summary>
	/// <param name="method">The HTTP method.</param>
	/// <param name="subdomain">The host label: <c>c</c>, <c>osu</c>, <c>api</c>, <c>a</c>, <c>assets</c> or <c>b</c>.</param>
	/// <param name="pathAndQuery">The path with its query, starting with a slash.</param>
	public HttpRequestMessage Request(HttpMethod method, string subdomain, string pathAndQuery)
	{
		var request = new HttpRequestMessage(method, $"https://{Connect}:{Port}{pathAndQuery}");
		request.Headers.Host = HostOf(subdomain);
		return request;
	}

	/// <summary>
	///     Creates the HTTP client the clients share: many connections per server and no certificate validation, for a
	///     bench with a self-signed certificate.
	/// </summary>
	/// <param name="timeout">How long a request may wait for its response headers.</param>
	public static HttpClient CreateHttpClient(TimeSpan timeout)
	{
		var handler = new SocketsHttpHandler
		{
			MaxConnectionsPerServer = int.MaxValue,
			PooledConnectionLifetime = TimeSpan.FromMinutes(10)
		};
		handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
		return new HttpClient(handler) { Timeout = timeout };
	}
}

/// <summary>The server's answer to a request: its status and what the body held.</summary>
/// <typeparam name="T">What the body is read as.</typeparam>
/// <param name="Status">The HTTP status code; a refusal is an answer, not an exception.</param>
/// <param name="Body">The body.</param>
public sealed record Reply<T>(HttpStatusCode Status, T Body)
{
	/// <summary>Whether the status is a success (2xx).</summary>
	public bool IsSuccess => (int)Status is >= 200 and < 300;
}

/// <summary>Sends a request and reads its answer in the three ways the clients need.</summary>
internal static class Exchange
{
	public static async Task<Reply<string>> TextAsync(HttpClient http, HttpRequestMessage request, CancellationToken ct)
	{
		using var response = await http.SendAsync(request, ct);
		return new Reply<string>(response.StatusCode, await response.Content.ReadAsStringAsync(ct));
	}

	/// <summary>Reads the body to its end without keeping it and tells how many bytes it had.</summary>
	public static async Task<Reply<long>> DrainAsync(HttpClient http, HttpRequestMessage request,
		CancellationToken ct)
	{
		using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
		await using var body = await response.Content.ReadAsStreamAsync(ct);
		var buffer = new byte[81920];
		long total = 0;
		int read;
		while ((read = await body.ReadAsync(buffer, ct)) > 0) total += read;

		return new Reply<long>(response.StatusCode, total);
	}
}