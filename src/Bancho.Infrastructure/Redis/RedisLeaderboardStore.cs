using Bancho.Application.Abstractions;
using StackExchange.Redis;

namespace Bancho.Infrastructure.Redis;

/// <inheritdoc cref="ILeaderboardStore" />
public sealed class RedisLeaderboardStore(IConnectionMultiplexer connection) : ILeaderboardStore
{
    private IDatabase Db => connection.GetDatabase();

    private static string GlobalKey(int mode) => $"bancho:leaderboard:{mode}";

    private static string CountryKey(int mode, string country) => $"bancho:leaderboard:{mode}:{country}";

    public async Task<int?> FetchGlobalRankAsync(int playerId, int mode, CancellationToken cancellationToken = default)
    {
        var rank = await Db.SortedSetRankAsync(GlobalKey(mode), playerId.ToString(), Order.Descending);
        return rank.HasValue ? (int)rank.Value + 1 : null;
    }

    public async Task<int?> FetchCountryRankAsync(int playerId, int mode, string country, CancellationToken cancellationToken = default)
    {
        var rank = await Db.SortedSetRankAsync(CountryKey(mode, country), playerId.ToString(), Order.Descending);
        return rank.HasValue ? (int)rank.Value + 1 : null;
    }

    public Task AddToGlobalLeaderboardAsync(int playerId, int mode, double score, CancellationToken cancellationToken = default) =>
        Db.SortedSetAddAsync(GlobalKey(mode), playerId.ToString(), score);

    public Task RemoveFromGlobalLeaderboardAsync(int playerId, int mode, CancellationToken cancellationToken = default) =>
        Db.SortedSetRemoveAsync(GlobalKey(mode), playerId.ToString());

    public Task AddToCountryLeaderboardAsync(int playerId, int mode, string country, double score, CancellationToken cancellationToken = default) =>
        Db.SortedSetAddAsync(CountryKey(mode, country), playerId.ToString(), score);

    public Task RemoveFromCountryLeaderboardAsync(int playerId, int mode, string country, CancellationToken cancellationToken = default) =>
        Db.SortedSetRemoveAsync(CountryKey(mode, country), playerId.ToString());
}
