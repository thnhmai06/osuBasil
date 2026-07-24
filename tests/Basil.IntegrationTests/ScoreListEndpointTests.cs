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
using Basil.Web.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers `GET /scores`, the new paginated score list. Deliberately not sharing a fixture with
///     <see cref="GetScoresEndpointTests" /> — that file covers the unrelated osu!-client
///     `osu-osz2-getscores.php` endpoint, a naming false-friend for this one.
/// </summary>
public class ScoreListEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly StubScoreRepository _scores = new();

    public ScoreListEndpointTests(WebApplicationFactory<Program> factory)
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
                services.AddSingleton<IUserRepository>(new NoopUserRepository());
                services.AddSingleton<IMapRepository>(new NoopMapRepository());
            });
        });
    }

    private static HttpRequestMessage MakeRequest(string path)
    {
        return new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
    }

    private static ScoreRow MakeRow(long id)
    {
        return new ScoreRow(
            id, null, null, new string('a', 32), 900_000, 98.5, 500, Mods.NoMod,
            300, 10, 5, 0, 0, 0, "S", GameMode.Standard, DateTime.UtcNow, 120_000,
            ClientFlags.Clean, 7, false, $"checksum-{id}", DateTime.UtcNow);
    }

    [Fact]
    public async Task GetScores_NoRows_ReturnsEmptyPage()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest("/scores"));
        var body = await response.Content.ReadFromJsonAsync<PagedResult<ScoreRow>>();

        response.EnsureSuccessStatusCode();
        Assert.NotNull(body);
        Assert.Equal(1, body!.Page);
        Assert.Equal(Pagination.DefaultPageSize, body.PageSize);
        Assert.Empty(body.Items);
        Assert.False(body.HasMore);
    }

    [Fact]
    public async Task GetScores_FewerThanPageSize_ReturnsAllWithoutHasMore()
    {
        _scores.Rows = [MakeRow(3), MakeRow(2), MakeRow(1)];

        var response = await _factory.CreateClient().SendAsync(MakeRequest("/scores"));
        var body = await response.Content.ReadFromJsonAsync<PagedResult<ScoreRow>>();

        response.EnsureSuccessStatusCode();
        Assert.Equal(3, body!.Count);
        Assert.False(body.HasMore);
        Assert.Equal([3L, 2L, 1L], body.Items.Select(r => r.Id));
    }

    [Fact]
    public async Task GetScores_PageSizeSmallerThanRows_SetsHasMoreTrue()
    {
        _scores.Rows = [MakeRow(3), MakeRow(2), MakeRow(1)];

        var response = await _factory.CreateClient().SendAsync(MakeRequest("/scores?page=1&pageSize=2"));
        var body = await response.Content.ReadFromJsonAsync<PagedResult<ScoreRow>>();

        response.EnsureSuccessStatusCode();
        Assert.Equal(2, body!.Count);
        Assert.True(body.HasMore);
        Assert.Equal([3L, 2L], body.Items.Select(r => r.Id));
    }

    private sealed class StubScoreRepository : IScoreRepository
    {
        public IReadOnlyList<ScoreRow> Rows { get; set; } = [];

        public Task<long> CreateAsync(ScoreInsertRow row, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> FetchCountAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.Count);
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
            throw new NotSupportedException();
        }

        public Task<ScoreRow?> FetchByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ScoreRow>> FetchPageAsync(int offset, int limit,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ScoreRow>>(Rows.Skip(offset).Take(limit).ToList());
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
