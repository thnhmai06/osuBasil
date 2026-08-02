using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

namespace Basil.Application.Abstractions.Multiplayer;

/// <summary>
///     Persists the durable record of a multiplayer room and the beatmaps played within it.
/// </summary>
/// <remarks>
///     The mapping is one multiplayer room to one match record, and one beatmap played to one round
///     record. The winning team is intentionally not persisted here and is computed on read
///     instead.
/// </remarks>
public interface IMatchRepository
{
	/// <summary>
	///     Creates a new match row.
	/// </summary>
	/// <param name="name">The name of the multiplayer room.</param>
	/// <param name="createdAt">The time the match was created, in UTC.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The id of the newly created match row.</returns>
	Task<int> CreateMatchAsync(
		string name, DateTime createdAt, CancellationToken cancellationToken = default);

	/// <summary>
	///     Marks a match as ended by setting its end time.
	/// </summary>
	/// <param name="matchId">The id of the match to update.</param>
	/// <param name="endedAt">The time the match ended, in UTC.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default);

	/// <summary>
	///     Creates a new round row for a beatmap played within a match.
	/// </summary>
	/// <param name="matchId">The id of the match the round belongs to.</param>
	/// <param name="roundIndex">The round's position within the match, starting at 0.</param>
	/// <param name="mapMd5">The content md5 of the beatmap played.</param>
	/// <param name="mode">The game mode the round was played in.</param>
	/// <param name="winCondition">The win condition in effect for the round.</param>
	/// <param name="teamType">The team setup the round was played under.</param>
	/// <param name="mods">The mods enforced for the round.</param>
	/// <param name="startedAt">The time the round started, in UTC.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The id of the newly created round row.</returns>
	/// <remarks>
	///     Only <paramref name="mapMd5" /> identifies the beatmap played. Every other beatmap fact is
	///     resolved live at report-build time by looking that md5 up through
	///     <see cref="Beatmaps.IBeatmapRepository" />, never denormalized onto the round itself.
	/// </remarks>
	Task<int> CreateRoundAsync(
		int matchId, int roundIndex, string mapMd5,
		GameMode mode, MatchWinCondition winCondition, MatchTeamType teamType,
		Mods mods, DateTime startedAt,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     Marks a round as ended.
	/// </summary>
	/// <param name="roundId">The id of the round to update.</param>
	/// <param name="endedAt">The time the round ended, in UTC.</param>
	/// <param name="aborted">
	///     <see langword="true" /> to record the round as aborted; otherwise, <see langword="false" />.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task SetRoundEndedAsync(int roundId, DateTime endedAt, bool aborted,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches a single match row by id.
	/// </summary>
	/// <param name="matchId">The id of the match to read.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The matching row, or <see langword="null" /> when no such match exists.</returns>
	Task<Match?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches every round row belonging to a match.
	/// </summary>
	/// <param name="matchId">The id of the match to read.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The match's rounds, ordered by <see cref="Round.RoundIndex" /> ascending.</returns>
	Task<IReadOnlyList<Round>> FetchRoundsAsync(int matchId, CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches every match row in the database.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Every stored match, newest id first.</returns>
	/// <remarks>
	///     Used by the match listing and deletion flows.
	/// </remarks>
	Task<IReadOnlyList<Match>> FetchAllMatchesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	///     Deletes a match and everything linked to it.
	/// </summary>
	/// <param name="matchId">The id of the match to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     Deletes the match's scores, events, and rounds, then the match record itself, in a single
	///     transaction.
	/// </remarks>
	Task DeleteMatchAsync(int matchId, CancellationToken cancellationToken = default);

	/// <summary>
	///     Records a match lifecycle event.
	/// </summary>
	/// <param name="row">The event to persist.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateEventAsync(MatchEvent row, CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches every event recorded for a match.
	/// </summary>
	/// <param name="matchId">The id of the match to read.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The match's events, ordered by timestamp ascending.</returns>
	Task<IReadOnlyList<MatchEvent>> FetchEventsAsync(int matchId, CancellationToken cancellationToken = default);

	/// <summary>
	///     Finds matches that were never marked as ended.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Every match with no end time, oldest id first.</returns>
	/// <remarks>
	///     Such rows are left behind by an unclean shutdown, for example a server crash or forced
	///     termination, and are the input to the startup recovery pass.
	/// </remarks>
	Task<IReadOnlyList<Match>> FetchUnrecoveredMatchesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	///     Finds rounds within a match that were never marked as ended.
	/// </summary>
	/// <param name="matchId">The id of the match to read.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The match's rounds with no end time, ordered by round index ascending.</returns>
	/// <remarks>
	///     Rounds can be left unfinished in the same unclean-shutdown scenario that produces
	///     unrecovered matches.
	/// </remarks>
	Task<IReadOnlyList<Round>> FetchUnrecoveredRoundsAsync(int matchId,
		CancellationToken cancellationToken = default);
}