using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Contracts.Repositories;

/// <summary>
///     The single source of truth for a user's cumulative score statistics, keyed by the user and
///     game mode pair rather than by a single id.
/// </summary>
public interface IUserStatsRepository
{
	/// <summary>Loads the statistics for a user in a game mode.</summary>
	/// <param name="userId">The id of the user.</param>
	/// <param name="mode">The game mode.</param>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns>
	///     The stored statistics, or <see cref="UserStats.Empty" /> when the user has not submitted a
	///     score in that mode yet. Never <see langword="null" />.
	/// </returns>
	ValueTask<UserStats> LoadAsync(int userId, GameMode mode, CancellationToken cancellationToken = default);

	/// <summary>Durably stores <paramref name="stats" />, replacing any existing statistics for its user and mode.</summary>
	/// <param name="stats">The statistics to store.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	Task SaveAsync(UserStats stats, CancellationToken cancellationToken = default);
}