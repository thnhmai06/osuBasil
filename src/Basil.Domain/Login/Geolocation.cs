using System.Globalization;
using System.Net;

namespace Basil.Domain.Login;

/// <summary>
///     Represents the geographic location of a client, derived from proxy headers.
/// </summary>
/// <param name="Latitude">The latitude of the client.</param>
/// <param name="Longitude">The longitude of the client.</param>
/// <param name="Country">The country of the client.</param>
public sealed record Geolocation(double Latitude, double Longitude, Country Country)
{
	/// <summary>
	///     Derives a geolocation from the request headers.
	/// </summary>
	/// <param name="headers">The request headers.</param>
	/// <returns>
	///     The parsed geolocation, or <see langword="null" /> when none of the expected headers are
	///     present or valid.
	/// </returns>
	public static Geolocation? From(IReadOnlyDictionary<string, string> headers)
	{
		return TryParseCloudflare(headers) ?? TryParseNginx(headers);
	}

	private static Geolocation? TryParseFromKeys(
		IReadOnlyDictionary<string, string> headers,
		string countryKey, string latKey, string lonKey)
	{
		if (!headers.TryGetValue(countryKey, out var countryValue)
		    || !headers.TryGetValue(latKey, out var latValue)
		    || !headers.TryGetValue(lonKey, out var lonValue)) return null;

		if (!Enum.TryParse<Country>(countryValue, true, out var countryCode)) return null;
		if (!double.TryParse(latValue, CultureInfo.InvariantCulture, out var latitude) ||
		    !double.TryParse(lonValue, CultureInfo.InvariantCulture, out var longitude)) return null;

		return new Geolocation(latitude, longitude, countryCode);
	}

	private static Geolocation? TryParseCloudflare(IReadOnlyDictionary<string, string> headers)
	{
		return TryParseFromKeys(headers, "CF-IPCountry", "CF-IPLatitude", "CF-IPLongitude");
	}

	private static Geolocation? TryParseNginx(IReadOnlyDictionary<string, string> headers)
	{
		return TryParseFromKeys(headers, "X-Country-Code", "X-Latitude", "X-Longitude");
	}

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
