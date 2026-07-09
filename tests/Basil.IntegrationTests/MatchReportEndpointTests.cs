using System.Net;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.IntegrationTests;

/// <summary>Covers the read-only slice of the api. host's TRT endpoint, GET /multi/{id}.</summary>
public class MatchReportEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly StubMatchPersistenceRepository _matchPersistence = new();
    private readonly StubScoreRepository _scores = new();

    public MatchReportEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ServerBehavior:Domain"] = "test.local",
                    ["Bot:CommandPrefix"] = "!",
                    ["ServerBehavior:MenuIconPath"] = "icon.png",
                    ["ServerBehavior:MenuOnclickUrl"] = "https://example.test",
                    ["Database:Path"] = ""
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IMatchPersistenceRepository>(_matchPersistence);
                services.AddSingleton<IScoreRepository>(_scores);
                services.AddSingleton<IMatchRegistry>(new StubMatchRegistry());
            });
        });
    }

    private static HttpRequestMessage MakeRequest(string path, string host = "api.test.local")
    {
        return new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = host } };
    }

    [Fact]
    public async Task GetMulti_UnknownMatch_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest("/multi/999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMulti_KnownMatch_ReturnsReportJson()
    {
        _matchPersistence.Match = new MatchRow(5, "Grand Finals", 0, 0, 0, 1,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null);
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest("/multi/5"));
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("\"Grand Finals\"", body);
        Assert.Contains("\"matchId\":5", body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubMatchPersistenceRepository : IMatchPersistenceRepository
    {
        public MatchRow? Match { get; set; }

        public Task<int> CreateMatchAsync(string name, int mode, int winCondition, int teamType, int hostId,
            DateTime createdAt, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> CreateRoundAsync(int matchId, int roundIndex, int beatmapId, string mapMd5, int mods,
            DateTime startedAt, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SetRoundEndedAsync(int roundId, DateTime endedAt, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<MatchRow?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Match?.Id == matchId ? Match : null);
        }

        public Task<IReadOnlyList<RoundRow>> FetchRoundsAsync(int matchId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RoundRow>>([]);
        }

        public Task<IReadOnlyList<MatchRow>> FetchAllMatchesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MatchRow>>(Match is null ? [] : [Match]);
        }

        public Task DeleteMatchAsync(int matchId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubScoreRepository : IScoreRepository
    {
        public Task<IReadOnlyList<BeatmapLeaderboardScoreRow>> FetchBeatmapLeaderboardScoresAsync(
            string mapMd5, GameMode mode, int userId, int? mods = null,
            IReadOnlySet<int>? friendIds = null, string? country = null, int limit = 50,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PersonalBestLeaderboardScoreRow?> FetchPersonalBestLeaderboardScoreAsync(string mapMd5,
            GameMode mode, int userId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> FetchPersonalBestLeaderboardRankAsync(string mapMd5,
            GameMode mode, long score,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<long> CreateAsync(ScoreInsertRow row, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ExistsByOnlineChecksumAsync(string onlineChecksum,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task MarkPreviousBestScoresSubmittedAsync(string mapMd5, int userId,
            GameMode mode, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<FirstPlaceScoreRow?> FetchFirstPlaceScoreAsync(string mapMd5,
            GameMode mode, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ScoreOwnerRow?> FetchOwnerAsync(long scoreId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<RoundScoreRow>> FetchByRoundIdAsync(int roundId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RoundScoreRow>>([]);
        }
    }

    private sealed class StubMatchRegistry : IMatchRegistry
    {
        public IReadOnlyList<MatchSession> All => [];

        public MatchSession? GetById(int id)
        {
            return null;
        }

        public MatchSession? GetByDbId(int dbId)
        {
            return null;
        }

        public MatchSession? TryCreate(Func<int, MatchSession> factory)
        {
            return null;
        }

        public void Remove(int id)
        {
        }
    }
}