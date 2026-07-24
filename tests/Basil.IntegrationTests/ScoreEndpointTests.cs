using System.Net;
using System.Net.Http.Json;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Web;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the new public `/scores` routes: `GET /scores/{scoreId}` (a score's full row, new — a score
///     used to only be visible embedded in a match report) and `GET /scores/{scoreId}/replay` (a direct
///     rename of the old bare `GET /replays/{scoreId}`).
/// </summary>
public class ScoreEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly StubScoreRepository _scores = new();
    private readonly StubReplayStorage _replayStorage = new();

    public ScoreEndpointTests(WebApplicationFactory<Program> factory)
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
                services.AddSingleton<IScoreRepository>(_scores);
                services.AddSingleton<IReplayStorage>(_replayStorage);
                services.AddSingleton<IUserRepository>(new NoopUserRepository());
                services.AddSingleton<IMapRepository>(new NoopMapRepository());
            });
        });
    }

    private static HttpRequestMessage MakeRequest(HttpMethod method, string path)
    {
        return new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
    }

    [Fact]
    public async Task GetScore_UnknownId_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetScore_KnownId_ReturnsFullRow()
    {
        _scores.Row = new ScoreRow(
            42, null, null, new string('a', 32), 900_000, 98.5, 500, Mods.Hidden,
            300, 10, 5, 0, 0, 0, "S", GameMode.Standard, DateTime.UtcNow, 120_000,
            ClientFlags.Clean, 7, false, "checksum", DateTime.UtcNow);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/42"));
        var envelope = await response.Content.ReadFromJsonAsync<Envelope<ScoreShape>>();

        response.EnsureSuccessStatusCode();
        var body = envelope!.Data;
        Assert.NotNull(body);
        Assert.Equal(42, body!.Id);
        Assert.Equal(900_000, body.Score);
        Assert.Equal(7, body.User.Id);
    }

    [Fact]
    public async Task GetReplay_UnknownScore_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/999/replay"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReplay_KnownScoreNoStoredReplay_ReturnsNotFound()
    {
        _scores.Owner = new ScoreOwnerRow(7, GameMode.Standard);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/42/replay"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReplay_Found_ReturnsReplayBytes()
    {
        _scores.Owner = new ScoreOwnerRow(7, GameMode.Standard);
        _replayStorage.Bytes = [1, 2, 3, 4];

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/42/replay"));
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/x-osu-replay", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, bytes);
    }

    private sealed record ScoreShape(long Id, long Score, UserBriefShape User);

    private sealed record UserBriefShape(int Id, string Name, string Country);

    private sealed class StubScoreRepository : IScoreRepository
    {
        public ScoreRow? Row { get; set; }
        public ScoreOwnerRow? Owner { get; set; }

        public Task<long> CreateAsync(ScoreInsertRow row, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> FetchCountAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Row is null ? 0 : 1);
        }

        public Task<bool> ExistsByOnlineChecksumAsync(string onlineChecksum,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<FirstPlaceScoreRow?> FetchFirstPlaceScoreAsync(string mapMd5, GameMode mode,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ScoreOwnerRow?> FetchOwnerAsync(long scoreId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Owner);
        }

        public Task<ScoreRow?> FetchByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Row);
        }

        public Task<IReadOnlyList<ScoreRow>> FetchPageAsync(int offset, int limit,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ScoreRow>>([]);
        }

        public Task<IReadOnlyList<RoundScoreRow>> FetchByRoundIdAsync(int roundId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RoundScoreRow>>([]);
        }

        public Task InvalidateByMapMd5Async(string mapMd5, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubReplayStorage : IReplayStorage
    {
        public byte[]? Bytes { get; set; }

        public Task<byte[]?> ReadAsync(long scoreId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Bytes);
        }

        public Task WriteAsync(long scoreId, byte[] data, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Stands in for the real DB-backed <see cref="IUserRepository" /> so an offline/unregistered id
    ///     referenced by these tests resolves to "no account" instead of hitting the real SQLite path
    ///     these tests otherwise never need a working database connection for.
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

    /// <summary>
    ///     Stands in for the real DB-backed <see cref="IMapRepository" /> so a score's stored `mapMd5`
    ///     resolves to "beatmap gone" (null) instead of hitting the real SQLite path these tests
    ///     otherwise never need a working database connection for.
    /// </summary>
    private sealed class NoopMapRepository : IMapRepository
    {
        public Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
            int? setId = null, bool includePrivate = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<Beatmap?>(null);

        public Task<Beatmap> UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(string? query, GameMode? mode, int offset,
            int amount, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyList<Beatmap>>>([]);

        public Task IncrementPlayCountsAsync(int mapId, bool passed, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task UpdateDiffAsync(int id, double diff, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, bool includePrivate = false,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Beatmap>>([]);
    }
}
