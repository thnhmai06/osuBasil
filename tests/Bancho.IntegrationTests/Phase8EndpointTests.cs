using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Bancho.IntegrationTests;

/// <summary>
///     Ported from the remaining app/api/domains/osu.py endpoints kept for the multiplayer/tourney-
///     only scope (getbeatmapinfo, lastfm, markasread, seasonal, bancho_connect, check-updates,
///     b.* redirect) plus the endpoints deliberately stubbed out (screenshot, favourites, rate,
///     comment, in-game registration, difficulty-rating) — see note.md for the scope decision.
///     Authenticated routes only need "player not online" coverage here (no DB access happens before
///     that check — see BanchoAuthenticationService); their real logic is unit-tested separately.
/// </summary>
public class Phase8EndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public Phase8EndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = Configure(factory);
    }

    private static WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> factory)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ServerBehavior:Domain"] = "test.local",
                    ["ServerBehavior:CommandPrefix"] = "!",
                    ["ServerBehavior:MenuIconUrl"] = "https://example.test/icon.png",
                    ["ServerBehavior:MenuOnclickUrl"] = "https://example.test"
                });
            });
        });
    }

    private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string host = "osu.test.local")
    {
        return new HttpRequestMessage(method, path) { Headers = { Host = host } };
    }

    [Fact]
    public async Task GetBeatmapInfo_PlayerNotOnline_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var request = MakeRequest(HttpMethod.Post, "/web/osu-getbeatmapinfo.php?u=nobody&h=x");
        request.Content = JsonContent("""{"Filenames":[],"Ids":[]}""");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LastFm_PlayerNotOnline_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/lastfm.php?b=a0&us=nobody&ha=x"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MarkAsRead_PlayerNotOnline_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response =
            await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/osu-markasread.php?u=nobody&h=x&channel=other"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Seasonal_ReturnsEmptyJsonArray_NoAuthNeeded()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/osu-getseasonal.php"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", body);
    }

    [Fact]
    public async Task BanchoConnect_ReturnsEmptyOk_NoAuthNeeded()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/bancho_connect.php?v=b20231231"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CheckUpdates_ReturnsEmptyOk_NoAuthNeeded()
    {
        var client = _factory.CreateClient();

        var response =
            await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/check-updates.php?action=check&stream=stable"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Screenshot_ReturnsNotAvailable()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/web/osu-screenshot.php"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not available", body);
    }

    [Fact]
    public async Task Favourites_GetAndAdd_ReturnEmptyOk()
    {
        var client = _factory.CreateClient();

        var getResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/osu-getfavourites.php"));
        var addResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/osu-addfavourite.php"));

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
    }

    [Fact]
    public async Task Rate_ReturnsNotRanked()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/osu-rate.php"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal("not ranked", body);
    }

    [Fact]
    public async Task Comment_ReturnsEmptyOk()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/web/osu-comment.php"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_ReturnsInGameRegistrationDisabled()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/users"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("In-game registration is disabled", body);
    }

    [Fact]
    public async Task DifficultyRating_RedirectsToRealOsu()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/difficulty-rating"));

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://osu.ppy.sh/difficulty-rating", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task BeatmapAssetHost_RedirectsToRealCdn()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/thumb/12345l.jpg", "b.test.local"));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("https://b.ppy.sh/thumb/12345l.jpg", response.Headers.Location!.ToString());
    }

    private static HttpContent JsonContent(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}