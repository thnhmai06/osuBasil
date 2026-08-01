using Basil.Domain.Beatmaps;

namespace Basil.Application.Abstractions.Scores;

/// <summary>
///     Provides player leaderboard ranks by ranked score per game mode.
/// </summary>
/// <remarks>
///     Ranks are computed live: a player's rank is the count of users whose ranked score in the
///     mode exceeds theirs, plus one. There is no separately maintained leaderboard to keep in
///     sync, so the add/remove methods below are no-ops retained for interface parity. Every mode,
///     including relax and autopilot, is ranked by raw ranked score, since Basil has no pp system.
/// </remarks>
public interface ILeaderboardStore
{
	/// <summary>
	///     Gets a player's 1-indexed global rank in a mode.
	/// </summary>
	/// <param name="playerId">The id of the player to rank.</param>
	/// <param name="mode">The game mode to rank in.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The player's global rank, or <see langword="null" /> when the player has no stats row.</returns>
	Task<int?> FetchGlobalRankAsync(int playerId, GameMode mode, CancellationToken cancellationToken = default);

	/// <summary>
	///     Gets a player's 1-indexed rank within their country in a mode.
	/// </summary>
	/// <param name="playerId">The id of the player to rank.</param>
	/// <param name="mode">The game mode to rank in.</param>
	/// <param name="country">The country acronym to rank within, for example <c>"us"</c>.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The player's country rank, or <see langword="null" /> when the player has no stats row.
	/// </returns>
	Task<int?> FetchCountryRankAsync(int playerId, GameMode mode, string country,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     A no-op retained for interface parity; global ranks are computed live, never stored.
	/// </summary>
	/// <param name="playerId">The id of the player.</param>
	/// <param name="mode">The game mode.</param>
	/// <param name="score">The ranked score value.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task AddToGlobalLeaderboardAsync(int playerId, GameMode mode, double score,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     A no-op retained for interface parity; global ranks are computed live, never stored.
	/// </summary>
	/// <param name="playerId">The id of the player.</param>
	/// <param name="mode">The game mode.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task RemoveFromGlobalLeaderboardAsync(int playerId, GameMode mode, CancellationToken cancellationToken = default);

	/// <summary>
	///     A no-op retained for interface parity; country ranks are computed live, never stored.
	/// </summary>
	/// <param name="playerId">The id of the player.</param>
	/// <param name="mode">The game mode.</param>
	/// <param name="country">The country acronym.</param>
	/// <param name="score">The ranked score value.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task AddToCountryLeaderboardAsync(int playerId, GameMode mode, string country, double score,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     A no-op retained for interface parity; country ranks are computed live, never stored.
	/// </summary>
	/// <param name="playerId">The id of the player.</param>
	/// <param name="mode">The game mode.</param>
	/// <param name="country">The country acronym.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task RemoveFromCountryLeaderboardAsync(int playerId, GameMode mode, string country,
		CancellationToken cancellationToken = default);
}