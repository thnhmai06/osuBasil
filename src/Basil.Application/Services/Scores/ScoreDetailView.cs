using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

namespace Basil.Application.Services.Scores;

/// <summary>
///     Represents a single submitted score as returned by the score lookup endpoints.
/// </summary>
/// <param name="Id">The database id of the score.</param>
/// <param name="User">The player who submitted the score.</param>
/// <param name="Beatmap">The beatmap the score was played on.</param>
/// <param name="Mode">The game mode the score was played in.</param>
/// <param name="Mods">The mods applied during the play.</param>
/// <param name="TotalScore">The total score of the play.</param>
/// <param name="Accuracy">The accuracy percentage of the play.</param>
/// <param name="MaxCombo">The maximum combo achieved during the play.</param>
/// <param name="Num300">The number of 300 hit judgments in the play.</param>
/// <param name="Num100">The number of 100 hit judgments in the play.</param>
/// <param name="Num50">The number of 50 hit judgments in the play.</param>
/// <param name="NumGeki">The number of geki hit judgments in the play.</param>
/// <param name="NumKatu">The number of katu hit judgments in the play.</param>
/// <param name="NumMiss">The number of miss hit judgments in the play.</param>
/// <param name="Grade">The letter grade achieved in the play.</param>
/// <param name="Perfect">A value that indicates whether the play was a perfect (full combo) play.</param>
/// <param name="PlayTime">The time the play was recorded by the client, in UTC.</param>
/// <param name="SubmittedAt">The time the submission was received by the server, in UTC.</param>
/// <param name="RoundId">The id of the match round the score is linked to, or <see langword="null" /> for a solo score.</param>
/// <param name="Team">The player's team within the round, or <see langword="null" /> for a solo score or a neutral slot.</param>
/// <remarks>
///     Every user and beatmap reference is embedded as a full view record (<see cref="UserBrief" />
///     and <see cref="BeatmapDetail" />), never a bare id or MD5. <see cref="Grade" /> keeps its
///     enum type rather than the raw storage string. <see cref="Beatmap" /> is always non-null,
///     because a submitted score is always recorded against a beatmap that existed in this server's
///     database at submission time.
/// </remarks>
public sealed record ScoreDetailView(
	long Id,
	UserBrief User,
	BeatmapDetail Beatmap,
	GameMode Mode,
	Mods Mods,
	long TotalScore,
	double Accuracy,
	int MaxCombo,
	int Num300,
	int Num100,
	int Num50,
	int NumGeki,
	int NumKatu,
	int NumMiss,
	Grade Grade,
	bool Perfect,
	DateTime PlayTime,
	DateTime SubmittedAt,
	int? RoundId,
	MatchTeam? Team);