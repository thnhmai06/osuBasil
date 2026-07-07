using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;
using OpenOsuTournament.Bancho.Application.Abstractions.Channels;
using OpenOsuTournament.Bancho.Application.Abstractions.Scores;
using OpenOsuTournament.Bancho.Application.Abstractions.Users;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Users;

namespace Bancho.IntegrationTests;

/// <summary>
///     Ported from app/api/domains/osu.py's getScores. Only wiring (auth gate, query-param binding,
///     dispatch to the right response formatter) is covered here — BeatmapLeaderboardService's own
///     branch logic and GetScoresResponseFormatter's byte format are already covered by their own
///     unit tests and not re-verified through HTTP.
/// </summary>
public class GetScoresEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly StubMapRepository _maps = new();

    public GetScoresEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
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
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IChannelRepository, NullChannelRepository>();
                services.AddSingleton<IUserRepository, StubUserRepository>();
                services.AddSingleton<IPasswordHasher, StubPasswordHasher>();
                services.AddSingleton<IMapRepository>(_maps);
                services.AddSingleton<IScoreRepository, StubScoreRepository>();
                services.AddSingleton<IRatingRepository, StubRatingRepository>();
            });
        });
    }

    private static Beatmap MakeBeatmap(string md5, RankedStatus status = RankedStatus.Ranked)
    {
        return new Beatmap(
            md5, 321, 100, "Artist", "Title", "Version", "Creator", DateTime.UtcNow, 100, 500,
            status, false, 0, 0, GameMode.VanillaOsu, 180.0, 4, 8, 9, 5, 6.5, "file.osu");
    }

    private static HttpRequestMessage MakeRequest(string queryString)
    {
        return new HttpRequestMessage(HttpMethod.Get, $"/web/osu-osz2-getscores.php?{queryString}")
            { Headers = { Host = "osu.test.local" } };
    }

    [Fact]
    public async Task PlayerNotOnline_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var request =
            MakeRequest($"us=nobody&ha=x&s=0&vv=4&v=1&c={new string('1', 32)}&f=file.osu&m=0&i=-1&mods=0&h=x&a=0");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongPassword_ReturnsUnauthorized()
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        sessionRegistry.Add(new PlayerSession(50, "cmyui-wrongpw", "tok", Privileges.Unrestricted, 0.0));
        var request =
            MakeRequest(
                $"us=cmyui-wrongpw&ha=wrong-md5&s=0&vv=4&v=1&c={new string('2', 32)}&f=file.osu&m=0&i=-1&mods=0&h=x&a=0");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownBeatmap_ReturnsNotSubmitted()
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        sessionRegistry.Add(new PlayerSession(51, "cmyui-notsubmitted", "tok2", Privileges.Unrestricted, 0.0));
        _maps.Beatmap = null;
        var request =
            MakeRequest(
                $"us=cmyui-notsubmitted&ha=correct-md5&s=0&vv=4&v=1&c={new string('3', 32)}&f=unknown.osu&m=0&i=-1&mods=0&h=x&a=0");

        var response = await _factory.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal("-1|false", body);
    }

    [Fact]
    public async Task OutOfRangeLeaderboardType_ReturnsBadRequest()
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        sessionRegistry.Add(new PlayerSession(53, "cmyui-badtype", "tok4", Privileges.Unrestricted, 0.0));
        var request =
            MakeRequest(
                $"us=cmyui-badtype&ha=correct-md5&s=0&vv=4&v=99&c={new string('5', 32)}&f=file.osu&m=0&i=-1&mods=0&h=x&a=0");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RankedBeatmap_NoScores_ReturnsFoundWithHeaderLine()
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        sessionRegistry.Add(new PlayerSession(52, "cmyui-found", "tok3", Privileges.Unrestricted, 0.0));
        var md5 = new string('4', 32);
        _maps.Beatmap = MakeBeatmap(md5);
        var request =
            MakeRequest($"us=cmyui-found&ha=correct-md5&s=0&vv=4&v=1&c={md5}&f=file.osu&m=0&i=-1&mods=0&h=x&a=0");

        var response = await _factory.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.StartsWith("2|false|321|100|0|0|", body);
    }

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

    private sealed class StubUserRepository : IUserRepository
    {
        public Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<User?>(null);
        }

        public Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<User?>(null);
        }

        public Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>("stored-hash");
        }

        public Task UpdateCountryAsync(int id, string country, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdatePrivilegesAsync(int id, int priv, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateApiKeyAsync(int id, string apiKey, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<User> CreateAsync(string name, string email, string pwBcrypt, string country,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubPasswordHasher : IPasswordHasher
    {
        public string Hash(byte[] passwordMd5)
        {
            throw new NotSupportedException();
        }

        public bool Verify(byte[] untrustedPasswordMd5, string trustedBcryptHash)
        {
            return Encoding.UTF8.GetString(untrustedPasswordMd5) == "correct-md5";
        }
    }

    private sealed class StubMapRepository : IMapRepository
    {
        public Beatmap? Beatmap { get; set; }

        public Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
            int? setId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Beatmap);
        }

        public Task UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
            string? query, GameMode? mode, RankedStatus? status, int offset, int amount,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<IReadOnlyList<Beatmap>>>([]);
        }

        public Task IncrementPlayCountsAsync(int mapId, bool passed, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubScoreRepository : IScoreRepository
    {
        public Task<IReadOnlyList<BeatmapLeaderboardScoreRow>> FetchBeatmapLeaderboardScoresAsync(
            string mapMd5, GameMode mode, int userId, int? mods = null, IReadOnlySet<int>? friendIds = null,
            string? country = null, int limit = 50, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BeatmapLeaderboardScoreRow>>([]);
        }

        public Task<PersonalBestLeaderboardScoreRow?> FetchPersonalBestLeaderboardScoreAsync(
            string mapMd5, GameMode mode, int userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PersonalBestLeaderboardScoreRow?>(null);
        }

        public Task<int> FetchPersonalBestLeaderboardRankAsync(
            string mapMd5, GameMode mode, long score, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<long> CreateAsync(ScoreInsertRow row, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }

        public Task<bool> ExistsByOnlineChecksumAsync(string onlineChecksum,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task MarkPreviousBestScoresSubmittedAsync(string mapMd5, int userId, GameMode mode,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<FirstPlaceScoreRow?> FetchFirstPlaceScoreAsync(string mapMd5, GameMode mode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<FirstPlaceScoreRow?>(null);
        }

        public Task<ScoreOwnerRow?> FetchOwnerAsync(long scoreId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ScoreOwnerRow?>(null);
        }
    }

    private sealed class StubRatingRepository : IRatingRepository
    {
        public Task<double> FetchAverageRatingAsync(string mapMd5, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0.0);
        }
    }
}