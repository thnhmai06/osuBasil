using System.Net;

namespace Basil.Domain.Login;

/// <summary>
///     Resolves the client's real IP address from reverse-proxy headers.
/// </summary>
public static class Geolocation
{
	/// <summary>
	///     Gets the client's real IP address from the request headers.
	/// </summary>
	/// <param name="headers">The request headers.</param>
	/// <returns>The real IP address of the client.</returns>
	public static IPAddress PhraseIpAddress(IReadOnlyDictionary<string, string> headers)
	{
		if (headers.TryGetValue("CF-Connecting-IP", out var cfIp)) return IPAddress.Parse(cfIp);

		var forwards = headers["X-Forwarded-For"].Split(',');
		var ipStr = forwards.Length != 1 ? forwards[0].Trim() : headers["X-Real-IP"];
		return IPAddress.Parse(ipStr);
	}
}