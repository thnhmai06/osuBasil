using System.Net;
using Bancho.Infrastructure.External;

namespace Bancho.Infrastructure.Tests.External;

/// <summary>Ported from app/state/services.py's _fetch_geoloc_from_ip (ip-api.com "line" format).</summary>
public class IpApiGeolocationProviderTests
{
    private sealed class FakeHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task FetchByIp_SuccessResponse_ReturnsGeolocation()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "success\nUS\n37.7749\n-122.4194");
        var provider = new IpApiGeolocationProvider(new HttpClient(handler));

        var geoloc = await provider.FetchByIpAsync(IPAddress.Parse("8.8.8.8"));

        Assert.NotNull(geoloc);
        Assert.Equal("us", geoloc!.CountryAcronym);
        Assert.Equal(225, geoloc.CountryNumeric);
        Assert.Equal(37.7749, geoloc.Latitude, precision: 4);
        Assert.Equal(-122.4194, geoloc.Longitude, precision: 4);
    }

    [Fact]
    public async Task FetchByIp_FailureStatus_ReturnsNull()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "fail\ninvalid query");
        var provider = new IpApiGeolocationProvider(new HttpClient(handler));

        Assert.Null(await provider.FetchByIpAsync(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public async Task FetchByIp_NonSuccessHttpStatus_ReturnsNull()
    {
        var handler = new FakeHandler(HttpStatusCode.InternalServerError, "");
        var provider = new IpApiGeolocationProvider(new HttpClient(handler));

        Assert.Null(await provider.FetchByIpAsync(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public async Task FetchByIp_PublicIp_IncludesIpInUrl()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "success\nUS\n1\n2");
        var provider = new IpApiGeolocationProvider(new HttpClient(handler));

        await provider.FetchByIpAsync(IPAddress.Parse("8.8.8.8"));

        Assert.Contains("8.8.8.8", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task FetchByIp_PrivateIp_OmitsIpFromUrl_LettingIpApiDetectCaller()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "success\nUS\n1\n2");
        var provider = new IpApiGeolocationProvider(new HttpClient(handler));

        await provider.FetchByIpAsync(IPAddress.Parse("192.168.1.1"));

        Assert.DoesNotContain("192.168.1.1", handler.LastRequestUri!.ToString());
    }
}
