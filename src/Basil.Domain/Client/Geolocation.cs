using System.Net;

namespace Basil.Domain.Client;

/// <summary>
///     Resolves the client's real IP address from reverse-proxy headers.
/// </summary>
/// <remarks>
///     Only useful behind a reverse proxy that sets the trust headers; the header values are taken
///     verbatim and are spoofable when the server is reachable directly.
/// </remarks>
public static class Geolocation
{
	/// <summary>
	///     Gets the client's real IP address from the request headers.
	/// </summary>
	/// <remarks>
	///     When the <c>CF-Connecting-IP</c> header is present, it wins. Otherwise, the
	///     <c>X-Forwarded-For</c> header is used: the first comma-separated entry when there are
	///     several, or the <c>X-Real-IP</c> header when there is exactly one entry. The resolved
	///     value is parsed as an IP address.
	/// </remarks>
	/// <param name="headers">The request headers.</param>
	/// <returns>The real IP address of the client.</returns>
	/// <exception cref="KeyNotFoundException">
	///     <c>X-Forwarded-For</c> is missing, or it contains a single entry while <c>X-Real-IP</c>
	///     is missing.
	/// </exception>
	/// <exception cref="FormatException">
	///     The resolved header value is not a valid IP address.
	/// </exception>
	public static IPAddress ParseIpAddress(IReadOnlyDictionary<string, string> headers)
	{
		if (headers.TryGetValue("CF-Connecting-IP", out var cfIp)) return IPAddress.Parse(cfIp);

		var forwards = headers["X-Forwarded-For"].Split(',');
		var ipStr = forwards.Length != 1 ? forwards[0].Trim() : headers["X-Real-IP"];
		return IPAddress.Parse(ipStr);
	}
}