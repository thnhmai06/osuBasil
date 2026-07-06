using System.Globalization;

namespace Bancho.Domain;

/// <summary>
/// Ported from app/state/services.py's _fetch_geoloc_from_headers (Cloudflare headers, falling
/// back to nginx headers). The ip-api HTTP fallback lives behind IGeolocationProvider
/// (Bancho.Application) since it requires network I/O.
/// </summary>
public static class GeolocationHeaderParser
{
    public static Geolocation? TryParse(IReadOnlyDictionary<string, string> headers) =>
        TryParseCloudflare(headers) ?? TryParseNginx(headers);

    private static Geolocation? TryParseCloudflare(IReadOnlyDictionary<string, string> headers) =>
        TryParse(headers, "CF-IPCountry", "CF-IPLatitude", "CF-IPLongitude");

    private static Geolocation? TryParseNginx(IReadOnlyDictionary<string, string> headers) =>
        TryParse(headers, "X-Country-Code", "X-Latitude", "X-Longitude");

    private static Geolocation? TryParse(IReadOnlyDictionary<string, string> headers, string countryKey, string latKey, string lonKey)
    {
        if (!headers.TryGetValue(countryKey, out var countryValue)
            || !headers.TryGetValue(latKey, out var latValue)
            || !headers.TryGetValue(lonKey, out var lonValue))
        {
            return null;
        }

        var countryAcronym = countryValue.ToLowerInvariant();
        if (!CountryCodes.ByAcronym.TryGetValue(countryAcronym, out var countryNumeric))
        {
            return null;
        }

        return new Geolocation(
            double.Parse(latValue, CultureInfo.InvariantCulture),
            double.Parse(lonValue, CultureInfo.InvariantCulture),
            countryAcronym,
            countryNumeric);
    }
}
