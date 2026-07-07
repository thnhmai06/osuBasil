using Bancho.Infrastructure.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Bancho.Infrastructure.Tests.Redis;

/// <summary>
///     Ported from app/repositories/leaderboard_ranks.py + the zadd/zrem call sites in
///     app/objects/player.py (restrict/unrestrict). bancho.py scores these zsets by `pp`; bancho-net
///     has no pp system, so every mode (including rx/ap) is scored by raw ranked score instead — the
///     zset mechanics (key shape, 1-indexed ZREVRANK) are otherwise identical.
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
        Assert.Null(await _store.FetchGlobalRankAsync(1, 0));
    }

    [Fact]
    public async Task AddAndFetchGlobalRank_ReturnsOneIndexedRank()
    {
        await _store.AddToGlobalLeaderboardAsync(1, 0, 500_000);
        await _store.AddToGlobalLeaderboardAsync(2, 0, 1_000_000);
        await _store.AddToGlobalLeaderboardAsync(3, 0, 250_000);

        // highest score = rank 1
        Assert.Equal(1, await _store.FetchGlobalRankAsync(2, 0));
        Assert.Equal(2, await _store.FetchGlobalRankAsync(1, 0));
        Assert.Equal(3, await _store.FetchGlobalRankAsync(3, 0));
    }

    [Fact]
    public async Task RemoveFromGlobalLeaderboard_PlayerNoLongerRanked()
    {
        await _store.AddToGlobalLeaderboardAsync(1, 0, 500_000);

        await _store.RemoveFromGlobalLeaderboardAsync(1, 0);

        Assert.Null(await _store.FetchGlobalRankAsync(1, 0));
    }

    [Fact]
    public async Task CountryLeaderboard_IsIndependentFromGlobal()
    {
        await _store.AddToGlobalLeaderboardAsync(1, 0, 500_000);
        await _store.AddToCountryLeaderboardAsync(1, 0, "us", 500_000);
        await _store.AddToCountryLeaderboardAsync(2, 0, "us", 900_000);

        Assert.Equal(2, await _store.FetchCountryRankAsync(1, 0, "us"));
        Assert.Null(await _store.FetchCountryRankAsync(1, 0, "jp"));

        await _store.RemoveFromCountryLeaderboardAsync(1, 0, "us");
        Assert.Null(await _store.FetchCountryRankAsync(1, 0, "us"));
        // global entry untouched by country removal
        Assert.NotNull(await _store.FetchGlobalRankAsync(1, 0));
    }
}