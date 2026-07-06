namespace Bancho.Domain.Tests;

/// <summary>
/// Ported from app/state/services.py's fetch_geoloc header path (Cloudflare headers, falling
/// back to nginx headers). bancho-net has no ip-api HTTP fallback (runs fully offline) — this
/// covers only the pure header parsing.
/// </summary>
public class GeolocationHeaderParserTests
{
    private static IReadOnlyDictionary<string, string> Headers(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void TryParse_CloudflareHeaders_ReturnsGeolocation()
    {
        var headers = Headers(("CF-IPCountry", "US"), ("CF-IPLatitude", "37.7749"), ("CF-IPLongitude", "-122.4194"));

        var geoloc = GeolocationHeaderParser.TryParse(headers);

        Assert.NotNull(geoloc);
        Assert.Equal("us", geoloc!.CountryAcronym);
        Assert.Equal(225, geoloc.CountryNumeric);
        Assert.Equal(37.7749, geoloc.Latitude, precision: 4);
        Assert.Equal(-122.4194, geoloc.Longitude, precision: 4);
    }

    [Fact]
    public void TryParse_NginxHeaders_UsedWhenCloudflareMissing()
    {
        var headers = Headers(("X-Country-Code", "JP"), ("X-Latitude", "35.0"), ("X-Longitude", "139.0"));

        var geoloc = GeolocationHeaderParser.TryParse(headers);

        Assert.NotNull(geoloc);
        Assert.Equal("jp", geoloc!.CountryAcronym);
        Assert.Equal(111, geoloc.CountryNumeric);
    }

    [Fact]
    public void TryParse_CloudflareTakesPrecedenceOverNginx()
    {
        var headers = Headers(
            ("CF-IPCountry", "US"), ("CF-IPLatitude", "1"), ("CF-IPLongitude", "2"),
            ("X-Country-Code", "JP"), ("X-Latitude", "3"), ("X-Longitude", "4"));

        var geoloc = GeolocationHeaderParser.TryParse(headers);

        Assert.Equal("us", geoloc!.CountryAcronym);
    }

    [Fact]
    public void TryParse_NoRecognizedHeaders_ReturnsNull()
    {
        Assert.Null(GeolocationHeaderParser.TryParse(Headers(("Some-Other-Header", "value"))));
    }

    [Fact]
    public void TryParse_PartialCloudflareHeaders_FallsThroughToNull()
    {
        // missing CF-IPLongitude — matches Python's `all(key in headers for key in (...))` check
        var headers = Headers(("CF-IPCountry", "US"), ("CF-IPLatitude", "1"));

        Assert.Null(GeolocationHeaderParser.TryParse(headers));
    }
}
