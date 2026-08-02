using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

namespace Basil.Application.Abstractions.Scores;

// TODO: Đưa Score record lên Domain

/// <summary>
///     Provides access to the Scores table.
/// </summary>
public interface IScoreRepository
{
	/// <summary>
	///     Inserts a new score row.
	/// </summary>
	/// <param name="row">The score data to persist.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The auto-increment id of the newly created row.</returns>
	Task<long> CreateAsync(ScoreInsertRow row, CancellationToken cancellationToken = default);

	/// <summary>
	///     Checks whether a score with the given online checksum already exists.
	/// </summary>
	/// <param name="onlineChecksum">The online checksum to look up.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> if a score with the checksum exists; otherwise,
	///     <see langword="false" />.
	/// </returns>
	/// <remarks>
	///     Feeds the duplicate-submission check. Only the existence is needed, so the lookup never
	///     materializes a full row.
	/// </remarks>
	Task<bool> CheckExistAsync(string onlineChecksum, CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches who owns the given score and in which mode it was played.
	/// </summary>
	/// <param name="scoreId">The id of the score.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The owner and mode, or <see langword="null" /> when no such score exists.</returns>
	Task<ScoreOwner?> FetchOwnerAsync(long scoreId, CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches one score's full row by id.
	/// </summary>
	/// <param name="id">The id of the score to read.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The full score row, or <see langword="null" /> when no score with this id exists.</returns>
	Task<ScoreRow?> FetchByIdAsync(long id, CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches a page of scores, newest first.
	/// </summary>
	/// <param name="offset">The number of scores to skip before returning results.</param>
	/// <param name="limit">The maximum number of scores to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The requested page of score rows.</returns>
	Task<IReadOnlyList<ScoreRow>> FetchPageAsync(int offset, int limit, CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches every score linked to a given round.
	/// </summary>
	/// <param name="roundId">The id of the round.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The round's scores, highest score first, with user names resolved.</returns>
	Task<IReadOnlyList<ScoreReport>> FetchByRoundAsync(int roundId, CancellationToken cancellationToken = default);

	/// <summary>
	///     Gets the total number of scores in the database.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The total score count.</returns>
	Task<int> FetchCountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     The subset of a score row needed to resolve who a replay belongs to.
/// </summary>
/// <param name="UserId">The id of the user who submitted the score.</param>
/// <param name="Mode">The game mode the score was played in.</param>
public sealed record ScoreOwner(int UserId, GameMode Mode);

/// <summary>
///     Every score submitted within one round, as read back for report building.
/// </summary>
/// <param name="Id">The unique identifier of the score.</param>
/// <param name="UserId">The id of the user who submitted the score.</param>
/// <param name="UserName">The name of the user who submitted the score.</param>
/// <param name="Team">The team the user played for, or <see langword="null" /> in head-to-head rounds.</param>
/// <param name="Mods">The mods applied to the play.</param>
/// <param name="Score">The achieved score.</param>
/// <param name="Accuracy">The achieved accuracy, as a fraction of 1.</param>
/// <param name="MaxCombo">The maximum combo achieved.</param>
/// <param name="N300">The count of 300 hits.</param>
/// <param name="N100">The count of 100 hits.</param>
/// <param name="N50">The count of 50 hits.</param>
/// <param name="NMiss">The count of misses.</param>
/// <param name="NGeki">The count of geki (elite 300) hits.</param>
/// <param name="NKatu">The count of katu (elite 100) hits.</param>
/// <param name="Grade">The letter grade of the play.</param>
/// <param name="Perfect">A value that indicates whether the play had a perfect combo.</param>
/// <param name="SubmittedAt">The time the score was submitted, in UTC.</param>
public sealed record ScoreReport(
	long Id,
	int UserId,
	string UserName,
	MatchTeam? Team,
	Mods Mods,
	long Score,
	double Accuracy,
	int MaxCombo,
	int N300,
	int N100,
	int N50,
	int NMiss,
	int NGeki,
	int NKatu,
	string Grade,
	bool Perfect,
	DateTime SubmittedAt);

/// <summary>
///     The fields written when a new score row is inserted.
/// </summary>
/// <param name="MapMd5">The content md5 of the beatmap played.</param>
/// <param name="Score">The achieved score.</param>
/// <param name="Accuracy">The achieved accuracy, as a fraction of 1.</param>
/// <param name="MaxCombo">The maximum combo achieved.</param>
/// <param name="Mods">The mods applied to the play.</param>
/// <param name="N300">The count of 300 hits.</param>
/// <param name="N100">The count of 100 hits.</param>
/// <param name="N50">The count of 50 hits.</param>
/// <param name="NMiss">The count of misses.</param>
/// <param name="NGeki">The count of geki (elite 300) hits.</param>
/// <param name="NKatu">The count of katu (elite 100) hits.</param>
/// <param name="Grade">The letter grade of the play.</param>
/// <param name="Mode">The game mode the score was played in.</param>
/// <param name="PlayTime">The time the beatmap was played.</param>
/// <param name="TimeElapsed">The elapsed play time in seconds.</param>
/// <param name="ClientFlags">The client flags reported with the submission.</param>
/// <param name="UserId">The id of the user who submitted the score.</param>
/// <param name="Perfect">A value that indicates whether the play had a perfect combo.</param>
/// <param name="OnlineChecksum">The online checksum sent by the client, used for duplicate detection.</param>
/// <param name="SubmittedAt">The time the score was submitted, in UTC.</param>
/// <param name="RoundId">The id of the multiplayer round the score belongs to, or <see langword="null" />.</param>
/// <param name="Team">The team the user played for, or <see langword="null" /> outside team play.</param>
/// <remarks>
///     The score column stores the raw value with no pp component: Basil has no pp system, so the
///     insert always writes 0 for it.
/// </remarks>
public record ScoreInsertRow(
	string MapMd5,
	long Score,
	double Accuracy,
	int MaxCombo,
	Mods Mods,
	int N300,
	int N100,
	int N50,
	int NMiss,
	int NGeki,
	int NKatu,
	string Grade,
	GameMode Mode,
	DateTime PlayTime,
	int TimeElapsed,
	ClientFlags ClientFlags,
	int UserId,
	bool Perfect,
	string OnlineChecksum,
	DateTime SubmittedAt,
	int? RoundId = null,
	MatchTeam? Team = null);

/// <summary>
///     One score's full row, as read back for the public score-detail route.
/// </summary>
/// <param name="Id">The unique identifier of the score.</param>
/// <param name="RoundId">The id of the multiplayer round the score belongs to, or <see langword="null" />.</param>
/// <param name="Team">The team the user played for, or <see langword="null" />.</param>
/// <param name="MapMd5">The content md5 of the beatmap played.</param>
/// <param name="Score">The achieved score.</param>
/// <param name="Accuracy">The achieved accuracy, as a fraction of 1.</param>
/// <param name="MaxCombo">The maximum combo achieved.</param>
/// <param name="Mods">The mods applied to the play.</param>
/// <param name="N300">The count of 300 hits.</param>
/// <param name="N100">The count of 100 hits.</param>
/// <param name="N50">The count of 50 hits.</param>
/// <param name="NMiss">The count of misses.</param>
/// <param name="NGeki">The count of geki (elite 300) hits.</param>
/// <param name="NKatu">The count of katu (elite 100) hits.</param>
/// <param name="Grade">The letter grade of the play.</param>
/// <param name="Mode">The game mode the score was played in.</param>
/// <param name="PlayTime">The time the beatmap was played.</param>
/// <param name="TimeElapsed">The elapsed play time in seconds.</param>
/// <param name="ClientFlags">The client flags reported with the submission.</param>
/// <param name="UserId">The id of the user who submitted the score.</param>
/// <param name="Perfect">A value that indicates whether the play had a perfect combo.</param>
/// <param name="OnlineChecksum">The online checksum sent by the client, used for duplicate detection.</param>
/// <param name="SubmittedAt">The time the score was submitted, in UTC.</param>
/// <remarks>
///     Whether the score's beatmap is still the one actually played is a read-time fact, decided by
///     whether <see cref="ScoreInsertRow.MapMd5" /> still resolves through the beatmap repository. It is not a
///     stored flag; the embed is built at the Web edge, not from this row alone.
/// </remarks>
public sealed record ScoreRow(
	long Id,
	int? RoundId,
	MatchTeam? Team,
	string MapMd5,
	long Score,
	double Accuracy,
	int MaxCombo,
	Mods Mods,
	int N300,
	int N100,
	int N50,
	int NMiss,
	int NGeki,
	int NKatu,
	string Grade,
	GameMode Mode,
	DateTime PlayTime,
	int TimeElapsed,
	ClientFlags ClientFlags,
	int UserId,
	bool Perfect,
	string OnlineChecksum,
	DateTime SubmittedAt) : ScoreInsertRow(MapMd5, Score, Accuracy, MaxCombo, Mods, N300, N100, N50, NMiss, NGeki,
	NKatu, Grade, Mode, PlayTime, TimeElapsed, ClientFlags, UserId, Perfect, OnlineChecksum, SubmittedAt, RoundId,
	Team);