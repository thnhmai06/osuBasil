using System.Net.Security;
using System.Net.Sockets;
using Basil.LoadTests.Configuration;
using Basil.LoadTests.Hosting;

namespace Basil.LoadTests.Client;

/// <summary>
///     Builds <see cref="HttpClient" />s that reach the server's <see cref="ServerEndpoint.ConnectAddress" />
///     regardless of what host name a request URI carries. The URI's host still drives the HTTP <c>Host</c>
///     header and TLS SNI — which is exactly what Basil's host-based routing (<c>c.</c>/<c>api.</c>/...)
///     needs — so no hosts-file entry and no DNS record for the configured domain are required.
/// </summary>
public sealed class BasilHttpClientFactory : IDisposable
{
	private readonly ServerEndpoint _endpoint;
	private readonly ClientSettings _settings;
	private readonly Lazy<HttpMessageHandler> _sharedHandler;

	public BasilHttpClientFactory(ServerEndpoint endpoint, ClientSettings settings)
	{
		_endpoint = endpoint;
		_settings = settings;
		_sharedHandler = new Lazy<HttpMessageHandler>(CreateHandler);
	}

	/// <summary>
	///     Creates a client for one virtual user. Under <see cref="ConnectionMode.Shared" /> every caller
	///     gets the same pooled handler; under <see cref="ConnectionMode.PerUser" /> each call gets its own
	///     handler and therefore its own socket — the mode that actually answers a connection-scalability
	///     question, and the first thing to hit the generator's own ephemeral-port ceiling.
	/// </summary>
	public HttpClient CreateClient()
	{
		var handler = _settings.ConnectionMode == ConnectionMode.Shared ? _sharedHandler.Value : CreateHandler();
		var client = new HttpClient(handler, _settings.ConnectionMode != ConnectionMode.Shared)
		{
			Timeout = _settings.RequestTimeout,
			DefaultRequestVersion = Version.Parse(_settings.HttpVersion),
			DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
		};
		return client;
	}

	/// <summary>Builds a request URI for the given bancho/API subdomain and path, e.g. <c>c.</c> + <c>/</c>.</summary>
	public Uri BuildUri(string subdomain, string pathAndQuery)
	{
		var host = string.IsNullOrEmpty(subdomain) ? _endpoint.Domain : $"{subdomain}.{_endpoint.Domain}";
		return new UriBuilder("https", host, _endpoint.Port, pathAndQuery).Uri;
	}

	public void Dispose()
	{
		if (_sharedHandler.IsValueCreated) _sharedHandler.Value.Dispose();
	}

	private HttpMessageHandler CreateHandler()
	{
		return new SocketsHttpHandler
		{
			// Always dial the resolved server address, no matter what host name the request URI carries
			// (that host name only matters for the Host header / TLS SNI, which drive Basil's routing).
			ConnectCallback = async (_, cancellationToken) =>
			{
				var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
				try
				{
					await socket.ConnectAsync(_endpoint.ConnectAddress, _endpoint.Port, cancellationToken);
					return new NetworkStream(socket, true);
				}
				catch
				{
					socket.Dispose();
					throw;
				}
			},
			SslOptions = new SslClientAuthenticationOptions
			{
				// basil-cert.pfx is self-signed and never intended to validate against the
				// synthetic FQDN used for local load testing; this handler is load-test-only.
				RemoteCertificateValidationCallback = (_, _, _, _) => true
			},
			// ponytail: bounded rather than infinite. A custom ConnectCallback bypassing the
			// handler's normal DNS/connect path made an occasional half-open pooled connection
			// (silently dead, but not detected as such before reuse) hang every request routed to
			// it until the client-side timeout — recycling connections periodically bounds the
			// damage from one going bad without giving up realistic keep-alive reuse.
			PooledConnectionLifetime = TimeSpan.FromSeconds(30),
			PooledConnectionIdleTimeout = TimeSpan.FromSeconds(10),
			MaxConnectionsPerServer = _settings.ConnectionMode == ConnectionMode.Shared
				? _settings.MaxConnectionsPerServer
				: 1
		};
	}
}
