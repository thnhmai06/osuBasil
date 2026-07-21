using System.Net;
using Basil.Domain.Login;

namespace Basil.Domain.Tests;

/// <summary>
///     Ported from app/state/services.py's fetch_geoloc header path (Cloudflare headers, falling
///     back to nginx headers). Basil has no ip-api HTTP fallback (runs fully offline) — this
///     covers only the pure header parsing.
/// </summary>
public class GeolocationTests
{
    private static Dictionary<string, string> Headers(params (string Key, string Value)[] pairs)
    {
        return pairs.ToDictionary(p => p.Key, p => p.Value);
    }

    [Fact]
    public void From_CloudflareHeaders_ReturnsGeolocation()
    {
        var headers = Headers(("CF-IPCountry", "US"), ("CF-IPLatitude", "37.7749"), ("CF-IPLongitude", "-122.4194"));

        var geoloc = Geolocation.From(headers);

        Assert.NotNull(geoloc);
        Assert.Equal(Country.Us, geoloc.Country);
        Assert.Equal(37.7749, geoloc.Latitude, 4);
        Assert.Equal(-122.4194, geoloc.Longitude, 4);
    }

    [Fact]
    public void From_NginxHeaders_UsedWhenCloudflareMissing()
    {
        var headers = Headers(("X-Country-Code", "JP"), ("X-Latitude", "35.0"), ("X-Longitude", "139.0"));

        var geoloc = Geolocation.From(headers);

        Assert.NotNull(geoloc);
        Assert.Equal(Country.Jp, geoloc.Country);
    }

    [Fact]
    public void From_CloudflareTakesPrecedenceOverNginx()
    {
        var headers = Headers(
            ("CF-IPCountry", "US"), ("CF-IPLatitude", "1"), ("CF-IPLongitude", "2"),
            ("X-Country-Code", "JP"), ("X-Latitude", "3"), ("X-Longitude", "4"));

        var geoloc = Geolocation.From(headers);

        Assert.Equal(Country.Us, geoloc!.Country);
    }

    [Fact]
    public void From_NoRecognizedHeaders_ReturnsNull()
    {
        Assert.Null(Geolocation.From(Headers(("Some-Other-Header", "value"))));
    }

    [Fact]
    public void From_PartialCloudflareHeaders_FallsThroughToNull()
    {
        // missing CF-IPLongitude — matches Python's `all(key in headers for key in (...))` check
        var headers = Headers(("CF-IPCountry", "US"), ("CF-IPLatitude", "1"));

        Assert.Null(Geolocation.From(headers));
    }
    
    [Fact]
    public void PhraseIpAddress_CfConnectingIpHeader_TakesPriority()
    {
        var headers = new Dictionary<string, string> { ["CF-Connecting-IP"] = "1.2.3.4", ["X-Real-IP"] = "9.9.9.9" };

        Assert.Equal(IPAddress.Parse("1.2.3.4"), Geolocation.PhraseIpAddress(headers));
    }

    [Fact]
    public void PhraseIpAddress_NoCloudflare_SingleForwardedFor_UsesXRealIp()
    {
        var headers = new Dictionary<string, string> { ["X-Forwarded-For"] = "5.6.7.8", ["X-Real-IP"] = "9.9.9.9" };

        Assert.Equal(IPAddress.Parse("9.9.9.9"), Geolocation.PhraseIpAddress(headers));
    }

    [Fact]
    public void PhraseIpAddress_NoCloudflare_MultipleForwardedFor_UsesFirstForwardedForEntry()
    {
        var headers = new Dictionary<string, string> { ["X-Forwarded-For"] = "5.6.7.8, 10.0.0.1" };

        Assert.Equal(IPAddress.Parse("5.6.7.8"), Geolocation.PhraseIpAddress(headers));
    }
}
