using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Domain.Multiplayer;

/// <summary>
///     A round record as read back for report purposes.
/// </summary>
/// <param name="Id">The unique identifier of the round.</param>
/// <param name="MatchId">The id of the match the round belongs to.</param>
/// <param name="RoundIndex">The round's position within the match, starting at 0.</param>
/// <param name="MapMd5">The content md5 of the beatmap played.</param>
/// <param name="Mode">The game mode the round was played in.</param>
/// <param name="WinCondition">The win condition in effect for the round.</param>
/// <param name="TeamType">The team setup the round was played under.</param>
/// <param name="Aborted">A value that indicates whether the round was aborted.</param>
/// <param name="Mods">The mods enforced for the round.</param>
/// <param name="StartedAt">The time the round started, in UTC.</param>
/// <param name="EndedAt">The time the round ended, in UTC, or <see langword="null" /> while open.</param>
/// <remarks>
///     Only <see cref="MapMd5" /> identifies the beatmap; every other beatmap fact is resolved live
///     at report-build time by looking the md5 up through the database, not stored here.
/// </remarks>
public sealed record Round(
	int Id,
	int MatchId,
	int RoundIndex,
	string MapMd5,
	GameMode Mode,
	MatchWinCondition WinCondition,
	MatchTeamType TeamType,
	bool Aborted,
	Mods Mods,
	DateTime StartedAt,
	DateTime? EndedAt);