using System.Net;
using System.Text;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.IntegrationTests;

/// <summary>
///     Integration tests for the osu-web HTTP endpoints that aren't covered by unit tests
///     (getbeatmapinfo, lastfm, markasread, seasonal, bancho_connect, check-updates,
///     b.* redirect) plus the endpoints deliberately stubbed out (screenshot, favourites, rate,
///     comment, in-game registration, difficulty-rating).
///     Authenticated routes only need "player not online" coverage here (no DB access happens before
///     that check — see BanchoAuthenticationService); their real logic is unit-tested separately.
/// </summary>
public class OsuWebEndpointIntegrationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = Configure(factory);

    private static WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> factory)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Server:Domain"] = "test.local",
                    ["Bot:CommandPrefix"] = "!",
                    ["Server:MenuIconPath"] = "icon.png",
                    ["Server:MenuOnclickUrl"] = "https://example.test",
                    ["Server:AdminKey"] = "",
                    ["Database:Path"] = ""
                });
            });
            builder.ConfigureServices(services =>
                services.AddSingleton<IMapRepository, NullMapRepository>());
        });
    }

    // Database:Path is "" for this test host (no real DB) — /difficulty-rating still calls
    // IMapRepository unconditionally, so it needs a stub rather than a real connection.
    private sealed class NullMapRepository : IMapRepository
    {
        public Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
            int? setId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Beatmap?>(null);
        }

        public Task UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(string? query, GameMode? mode,
            RankedStatus? status, int offset, int amount, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<IReadOnlyList<Beatmap>>>([]);
        }

        public Task IncrementPlayCountsAsync(int mapId, bool passed, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task UpdateDiffAsync(int id, double diff, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Beatmap>>([]);
        }
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
    public async Task Register_NoAdminKey_ReturnsInGameRegistrationDisabled()
    {
        var client = _factory.CreateClient();

        var request = MakeRequest(HttpMethod.Post, "/users");
        request.Content = new StringContent(
            "user[username]=Player1&user[user_email]=anything@test.com&user[password]=hunter2",
            Encoding.UTF8,
            "application/x-www-form-urlencoded");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("In-game registration is disabled", body);
    }

    [Fact]
    public async Task DifficultyRating_NoBeatmapId_ReturnsExplanatoryMessage()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/difficulty-rating"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("beatmap id", body);
    }

    [Fact]
    public async Task DifficultyRating_UnknownBeatmapId_ReturnsNotFound()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/difficulty-rating?b=999999999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
