using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Bancho.IntegrationTests;

/// <summary>
///     Ported from app/api/domains/osu.py's get_osz/get_updated_beatmap. This server has no local
///     .osz/.osu file storage and no internet default — see MirrorOptions' doc comment. Both
///     endpoints report unavailability rather than reaching out to the internet by default;
///     /d/{set_id} only redirects if an operator explicitly configures MirrorOptions:DownloadEndpoint.
/// </summary>
public class BeatmapRedirectEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BeatmapRedirectEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> factory,
        string? downloadEndpoint = null)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["ServerBehavior:Domain"] = "test.local",
                    ["ServerBehavior:CommandPrefix"] = "!",
                    ["ServerBehavior:MenuIconUrl"] = "https://example.test/icon.png",
                    ["ServerBehavior:MenuOnclickUrl"] = "https://example.test"
                };
                if (downloadEndpoint is not null) settings["Mirror:DownloadEndpoint"] = downloadEndpoint;

                config.AddInMemoryCollection(settings);
            });
        });
    }

    private static HttpRequestMessage MakeRequest(string path)
    {
        return new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "osu.test.local" } };
    }

    [Fact]
    public async Task Download_NoDownloadEndpointConfigured_ReturnsUnavailableMessage_NoRedirect()
    {
        var client = Configure(_factory)
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(MakeRequest("/d/12345"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("not available", body);
    }

    [Fact]
    public async Task Download_EndpointConfigured_RedirectsWithVideoFlag()
    {
        var client = Configure(_factory, "https://mirror.local/d")
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(MakeRequest("/d/12345"));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("https://mirror.local/d/12345?n=1", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Download_NoVideoFlagSuffix_StripsNAndSetsNZero()
    {
        var client = Configure(_factory, "https://mirror.local/d")
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(MakeRequest("/d/12345n"));

        Assert.Equal("https://mirror.local/d/12345?n=0", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task MapFile_ReturnsNotFound_WhenFilenameUnknown()
    {
        var client = Configure(_factory)
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(MakeRequest("/web/maps/Some%20Map.osu"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}