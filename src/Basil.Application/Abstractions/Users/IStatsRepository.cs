using Basil.Domain.Beatmaps;

namespace Basil.Application.Abstractions.Users;

/// <summary>
///     Fixed gameplay stats — seeded once, never updated after score submission (server does not
///     track singleplayer ranking/progression). Ported from app/repositories/stats.py's Stats
///     dataclass (Basil: no `pp` column usage).
/// </summary>
public sealed record Stats(int Id, GameMode Mode, long Tscore, long Rscore, int Plays, double Acc);

/// <summary>
///     Ported from app/repositories/stats.py's StatsRepository, scoped to what login needs.
/// </summary>
public interface IStatsRepository
{
    Task<IReadOnlyList<Stats>> FetchAllForUserAsync(int userId, CancellationToken cancellationToken = default);
}
