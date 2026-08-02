using Basil.Domain.Beatmaps;

namespace Basil.Application.Abstractions.Users;

/// <summary>
///     A user's overall stats for one game mode.
/// </summary>
/// <param name="Id">The id of the user the stats belong to.</param>
/// <param name="Mode">The game mode the stats apply to.</param>
/// <param name="TotalScore">The user's total accumulated score in the mode.</param>
/// <param name="RankedScore">The user's ranked score in the mode, the basis for leaderboard rank.</param>
/// <param name="Plays">The number of plays submitted in the mode.</param>
/// <remarks>
///     Total score, ranked score, and plays are bumped on every score submission, see
///     <see cref="IUserStatRepository.IncrementAsync" />. Accuracy is intentionally absent: this
///     server reports a fixed 100% rather than computing and storing a real weighted accuracy.
/// </remarks>
public sealed record Stats(int Id, GameMode Mode, long TotalScore, long RankedScore, int Plays);

/// <summary>
///     Provides per-user, per-mode stats, scoped to what login and score submission need.
/// </summary>
public interface IUserStatRepository
{
	/// <summary>
	///     Fetches a user's stats across every game mode.
	/// </summary>
	/// <param name="userId">The id of the user to read.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The user's per-mode stats rows, in no particular order.</returns>
	Task<IReadOnlyList<Stats>> FetchAllForUserAsync(int userId, CancellationToken cancellationToken = default);

	/// <summary>
	///     Atomically bumps a user's per-mode stats after a score submission.
	/// </summary>
	/// <param name="userId">The id of the user to update.</param>
	/// <param name="mode">The game mode of the submitted play.</param>
	/// <param name="totalScoreDelta">The amount to add to the total score.</param>
	/// <param name="rankedScoreDelta">The amount to add to the ranked score.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     This upserts the stats on a conflict keyed by user and mode, so a user's first-ever play
	///     in a mode creates the stats here: only the bot account's are pre-created, real users get
	///     none until their first submission. Plays always increases by one. The total score delta
	///     is always the submitted score; the ranked score delta is the submitted score only when
	///     the submission was linked to an active multiplayer round, and 0 otherwise.
	/// </remarks>
	Task IncrementAsync(int userId, GameMode mode, long totalScoreDelta, long rankedScoreDelta,
		CancellationToken cancellationToken = default);
}