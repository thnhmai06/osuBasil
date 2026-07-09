using System.Net;
using Basil.Application.Abstractions.Channels;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.IntegrationTests;

/// <summary>
///     bancho.py routes by hostname (app/api/init_api.py:init_routes) rather than by path prefix:
///     c./ce./c4./c5./c6.{domain} -> bancho realtime, osu.{domain} -> osu! web endpoints,
///     b.{domain} -> beatmap assets, api.{domain} -> developer API. Both the configured DOMAIN and
///     the hardcoded ppy.sh are registered for every group. This test locks in that routing shape
///     before any real endpoint logic exists.
/// </summary>
public class HostRoutingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HostRoutingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Server:Domain"] = "test.local",
                    ["Bot:CommandPrefix"] = "!",
                    ["Server:MenuIconPath"] = "icon.png",
                    ["Server:MenuOnclickUrl"] = "https://example.test",
                    ["Database:Path"] = ""
                });
            });
            builder.ConfigureServices(services =>
                services.AddSingleton<IChannelRepository, NullChannelRepository>());
        });
    }

    [Theory]
    [InlineData("c.test.local")]
    [InlineData("ce.test.local")]
    [InlineData("c4.test.local")]
    [InlineData("c5.test.local")]
    [InlineData("c6.test.local")]
    [InlineData("c.ppy.sh")]
    [InlineData("ce.ppy.sh")]
    [InlineData("c4.ppy.sh")]
    [InlineData("c5.ppy.sh")]
    [InlineData("c6.ppy.sh")]
    public async Task BanchoSubdomains_RouteToChoGroup(string host)
    {
        var client = _factory.CreateClient();
        var response = await SendWithHost(client, host);

        response.EnsureSuccessStatusCode();
        Assert.Equal("cho", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("osu.test.local")]
    [InlineData("osu.ppy.sh")]
    public async Task OsuSubdomain_RoutesToOsuWebGroup(string host)
    {
        var client = _factory.CreateClient();
        var response = await SendWithHost(client, host);

        response.EnsureSuccessStatusCode();
        Assert.Equal("osu", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("b.test.local")]
    [InlineData("b.ppy.sh")]
    public async Task BeatmapAssetSubdomain_RoutesToMapGroup(string host)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await SendWithHost(client, host);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("https://b.ppy.sh/", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("api.test.local")]
    [InlineData("api.ppy.sh")]
    public async Task ApiSubdomain_RoutesToApiGroup(string host)
    {
        var client = _factory.CreateClient();
        var response = await SendWithHost(client, host);

        response.EnsureSuccessStatusCode();
        Assert.Equal("api", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnrecognizedHost_Returns404()
    {
        var client = _factory.CreateClient();
        var response = await SendWithHost(client, "unknown.test.local");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendWithHost(HttpClient client, string host)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = host;
        return await client.SendAsync(request);
    }

    /// <summary>Avoids the real MySQL repo so Program.cs's startup channel-seeding doesn't need a live DB.</summary>
    private sealed class NullChannelRepository : IChannelRepository
    {
        public Task<IReadOnlyList<Channel>> FetchAllAutoJoinAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Channel>>([]);
        }

        public Task<Channel?> FetchOneByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Channel?>(null);
        }
    }
}