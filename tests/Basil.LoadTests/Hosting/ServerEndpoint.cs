using System.Net;

namespace Basil.LoadTests.Hosting;

/// <summary>
///     Where scenarios send requests. <see cref="Domain" /> is used for the HTTP Host header and TLS
///     SNI (host-based routing needs the right subdomain there); <see cref="ConnectAddress" /> is the
///     actual socket address a client dials, which the <see cref="Client.BasilHttpClientFactory" />'s
///     connect callback substitutes in regardless of what the request URI's host resolves to.
/// </summary>
/// <param name="Domain">The configured FQDN (e.g. <c>basil.local</c>); subdomains are <c>c.</c>/<c>api.</c>/etc. of this.</param>
/// <param name="Port">The HTTPS port to connect to.</param>
/// <param name="ConnectAddress">The IP address the client actually dials.</param>
public sealed record ServerEndpoint(string Domain, int Port, IPAddress ConnectAddress);
