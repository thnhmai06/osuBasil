using System.Net;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>Covers the read-only slice of the api. host's TRT endpoint, GET /matches/{matchId}.</summary>
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
                    ["Basil:Server:Domain"] = "test.local",
                    ["Basil:Bot:CommandPrefix"] = "!",
                    ["Basil:Server:MenuIconPath"] = "icon.png",
                    ["Basil:Server:MenuOnclickUrl"] = "https://example.test"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
                services.AddSingleton<IMatchPersistenceRepository>(_matchPersistence);
                services.AddSingleton<IScoreRepository>(_scores);
                services.AddSingleton<IMatchRegistry>(new StubMatchRegistry());
                services.AddSingleton<IUserRepository>(new NoopUserRepository());
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

        var response = await client.SendAsync(MakeRequest("/matches/999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMulti_KnownMatch_ReturnsReportJson()
    {
        _matchPersistence.Match = new MatchRow(5, "Grand Finals",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null);
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest("/matches/5"));
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("\"Grand Finals\"", body);
        Assert.Contains("\"matchId\":5", body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubMatchPersistenceRepository : IMatchPersistenceRepository
    {
        public MatchRow? Match { get; set; }

        public Task<int> CreateMatchAsync(string name, DateTime createdAt, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> CreateRoundAsync(int matchId, int roundIndex, string mapMd5,
            GameMode mode, MatchWinCondition winCondition, MatchTeamType teamType,
            Mods mods, DateTime startedAt, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SetRoundEndedAsync(int roundId, DateTime endedAt, bool aborted, CancellationToken cancellationToken = default)
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

        public Task CreateEventAsync(MatchEventRow row, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MatchEventRow>> FetchEventsAsync(int matchId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MatchEventRow>>([]);
        public Task<IReadOnlyList<MatchRow>> FetchUnrecoveredMatchesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MatchRow>>([]);
        public Task<IReadOnlyList<RoundRow>> FetchUnrecoveredRoundsAsync(int matchId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RoundRow>>([]);
    }

    private sealed class StubScoreRepository : IScoreRepository
    {
        public Task<long> CreateAsync(ScoreInsertRow row, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> FetchCountAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<bool> ExistsByOnlineChecksumAsync(string onlineChecksum,
            CancellationToken cancellationToken = default)
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

        public Task<ScoreRow?> FetchByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ScoreRow>> FetchPageAsync(int offset, int limit,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<RoundScoreRow>> FetchByRoundIdAsync(int roundId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RoundScoreRow>>([]);
        }

        public Task InvalidateByMapMd5Async(string mapMd5, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
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

    /// <summary>
    ///     Stands in for the real DB-backed <see cref="IUserRepository" /> so an offline/unregistered id
    ///     referenced by these tests resolves to "no account" — UserBriefResolver's documented fallback —
    ///     instead of hitting the real SQLite path these tests otherwise never need a working database
    ///     connection for.
    /// </summary>
    private sealed class NoopUserRepository : IUserRepository
    {
        public Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task UpdateCountryAsync(int id, string country, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdatePrivilegesAsync(int id, UserPrivileges priv, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<User?> CreateAsync(string name, string pwBcrypt, string country, UserPrivileges? priv = null,
            CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);

        public Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<User>>([]);
    }
}