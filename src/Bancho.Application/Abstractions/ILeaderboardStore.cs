namespace Bancho.Application.Abstractions;

/// <summary>
/// Player leaderboard positions, stored in Redis sorted sets. Ported from
/// app/repositories/leaderboard_ranks.py + the zadd/zrem call sites in app/objects/player.py
/// (restrict/unrestrict). bancho.py scores these zsets by pp; bancho-net has no pp system, so
/// every mode (including rx/ap) is scored by raw ranked score instead.
/// </summary>
public interface ILeaderboardStore
{
    /// <summary>Fetches a player's 1-indexed global rank for a mode, if ranked.</summary>
    Task<int?> FetchGlobalRankAsync(int playerId, int mode, CancellationToken cancellationToken = default);

    /// <summary>Fetches a player's 1-indexed country rank for a mode, if ranked.</summary>
    Task<int?> FetchCountryRankAsync(int playerId, int mode, string country, CancellationToken cancellationToken = default);

    Task AddToGlobalLeaderboardAsync(int playerId, int mode, double score, CancellationToken cancellationToken = default);

    Task RemoveFromGlobalLeaderboardAsync(int playerId, int mode, CancellationToken cancellationToken = default);

    Task AddToCountryLeaderboardAsync(int playerId, int mode, string country, double score, CancellationToken cancellationToken = default);

    Task RemoveFromCountryLeaderboardAsync(int playerId, int mode, string country, CancellationToken cancellationToken = default);
}
