using Bancho.Infrastructure.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Bancho.Infrastructure.Tests.Redis;

/// <summary>
/// Ported from app/repositories/leaderboard_ranks.py + the zadd/zrem call sites in
/// app/objects/player.py (restrict/unrestrict). bancho.py scores these zsets by `pp`; bancho-net
/// has no pp system, so every mode (including rx/ap) is scored by raw ranked score instead — the
/// zset mechanics (key shape, 1-indexed ZREVRANK) are otherwise identical.
/// </summary>
public class RedisLeaderboardStoreTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4").Build();
    private ConnectionMultiplexer _connection = null!;
    private RedisLeaderboardStore _store = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _store = new RedisLeaderboardStore(_connection);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task FetchGlobalRank_NoEntries_ReturnsNull()
    {
        Assert.Null(await _store.FetchGlobalRankAsync(playerId: 1, mode: 0));
    }

    [Fact]
    public async Task AddAndFetchGlobalRank_ReturnsOneIndexedRank()
    {
        await _store.AddToGlobalLeaderboardAsync(playerId: 1, mode: 0, score: 500_000);
        await _store.AddToGlobalLeaderboardAsync(playerId: 2, mode: 0, score: 1_000_000);
        await _store.AddToGlobalLeaderboardAsync(playerId: 3, mode: 0, score: 250_000);

        // highest score = rank 1
        Assert.Equal(1, await _store.FetchGlobalRankAsync(playerId: 2, mode: 0));
        Assert.Equal(2, await _store.FetchGlobalRankAsync(playerId: 1, mode: 0));
        Assert.Equal(3, await _store.FetchGlobalRankAsync(playerId: 3, mode: 0));
    }

    [Fact]
    public async Task RemoveFromGlobalLeaderboard_PlayerNoLongerRanked()
    {
        await _store.AddToGlobalLeaderboardAsync(playerId: 1, mode: 0, score: 500_000);

        await _store.RemoveFromGlobalLeaderboardAsync(playerId: 1, mode: 0);

        Assert.Null(await _store.FetchGlobalRankAsync(playerId: 1, mode: 0));
    }

    [Fact]
    public async Task CountryLeaderboard_IsIndependentFromGlobal()
    {
        await _store.AddToGlobalLeaderboardAsync(playerId: 1, mode: 0, score: 500_000);
        await _store.AddToCountryLeaderboardAsync(playerId: 1, mode: 0, country: "us", score: 500_000);
        await _store.AddToCountryLeaderboardAsync(playerId: 2, mode: 0, country: "us", score: 900_000);

        Assert.Equal(2, await _store.FetchCountryRankAsync(playerId: 1, mode: 0, country: "us"));
        Assert.Null(await _store.FetchCountryRankAsync(playerId: 1, mode: 0, country: "jp"));

        await _store.RemoveFromCountryLeaderboardAsync(playerId: 1, mode: 0, country: "us");
        Assert.Null(await _store.FetchCountryRankAsync(playerId: 1, mode: 0, country: "us"));
        // global entry untouched by country removal
        Assert.NotNull(await _store.FetchGlobalRankAsync(playerId: 1, mode: 0));
    }
}
