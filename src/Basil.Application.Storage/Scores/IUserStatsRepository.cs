using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Storage.Scores;

/// <summary>
///     The single source of truth for a user's cumulative score statistics, keyed by the user and
///     game mode pair rather than by a single id.
/// </summary>
public interface IUserStatsRepository
{
	/// <summary>Gets the statistics for a user in a game mode.</summary>
	/// <param name="user">The user.</param>
	/// <param name="mode">The game mode.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored statistics, or statistics with zero totals when the user has not submitted a score in that mode yet. Never <see langword="null" />.</returns>
	ValueTask<UserStats> GetAsync(User user, GameMode mode, CancellationToken cancellationToken = default);

	/// <summary>Stores statistics, replacing any statistics stored for the same user and mode.</summary>
	/// <param name="stats">The statistics to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateOrUpdateAsync(UserStats stats, CancellationToken cancellationToken = default);
}