using Basil.Domain.Beatmaps;

namespace Basil.Application.Abstractions.Users;

/// <summary>
///     Per-mode overall stats — TotalScore/RankedScore/Plays are bumped on every score submission
///     (see <see cref="IncrementAsync" />). Accuracy is intentionally not part of this record: this
///     server reports a fixed 100% rather than computing/storing a real weighted accuracy. Ported
///     from app/repositories/stats.py's Stats dataclass (Basil: no `pp` column usage).
/// </summary>
public sealed record Stats(int Id, GameMode Mode, long TotalScore, long RankedScore, int Plays);

/// <summary>
///     Ported from app/repositories/stats.py's StatsRepository, scoped to what login/score submission need.
/// </summary>
public interface IStatsRepository
{
	Task<IReadOnlyList<Stats>> FetchAllForUserAsync(int userId, CancellationToken cancellationToken = default);

	/// <summary>
	///     Atomically bumps a user's per-mode stats after a score submission — upserts the row if this
	///     is the user's first-ever play in <paramref name="mode" /> (only BasilBot's rows are seeded;
	///     real users get no UserStats row until here). Plays always +1; <paramref name="totalScoreDelta" />
	///     is always the submitted score; <paramref name="rankedScoreDelta" /> is the submitted score
	///     when the submission was linked to an active multiplayer round, 0 otherwise.
	/// </summary>
	Task IncrementAsync(int userId, GameMode mode, long totalScoreDelta, long rankedScoreDelta,
		CancellationToken cancellationToken = default);
}
