using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Application.Services.Scores;

/// <summary>
///     A single submitted score as returned by the score lookup endpoints.
/// </summary>
/// <param name="Id">The database id of the score.</param>
/// <param name="User">The userSession who submitted the score.</param>
/// <param name="Beatmap">The beatmap the score was played on.</param>
/// <param name="Mode">The game mode the score was played in.</param>
/// <param name="Mods">The mods applied during the play.</param>
/// <param name="TotalScore">The total score of the play.</param>
/// <param name="Accuracy">The accuracy percentage of the play.</param>
/// <param name="MaxCombo">The maximum combo achieved during the play.</param>
/// <param name="Num300">The number of 300 judgments.</param>
/// <param name="Num100">The number of 100 judgments.</param>
/// <param name="Num50">The number of 50 judgments.</param>
/// <param name="NumGeki">The number of geki judgments.</param>
/// <param name="NumKatu">The number of katu judgments.</param>
/// <param name="NumMiss">The number of misses.</param>
/// <param name="Grade">The awarded grade.</param>
/// <param name="Perfect">Whether the play was perfect (full combo).</param>
/// <param name="PlayTime">The time the client recorded the play, in UTC.</param>
/// <param name="SubmittedAt">The time the server received the submission, in UTC.</param>
/// <param name="RoundId">The id of the match round the score is linked to, or <see langword="null" /> for a solo score.</param>
/// <param name="Team">
///     The userSession's team within the round, or <see langword="null" /> for a solo score or a neutral
///     slot.
/// </param>
/// <remarks>
///     Every user and beatmap reference is embedded as a full view record (<see cref="UserBrief" />
///     and <see cref="BeatmapDetail" />), never a bare id or MD5. <see cref="Grade" /> serializes as
///     its numeric enum value.
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
	DateTimeOffset PlayTime,
	DateTimeOffset SubmittedAt,
	int? RoundId,
	MatchTeam? Team);