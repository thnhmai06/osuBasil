using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

namespace Basil.Application.Services.Scores;

/// <summary>
///     Wire shape for `GET /scores` and `GET /scores/{scoreId}` — every user/beatmap reference
///     embedded (never a bare id/md5), <see cref="Grade" /> enum-typed rather than the raw storage
///     string. <see cref="Beatmap" /> is non-null: a submitted score is always recorded against a
///     beatmap that existed in this server's DB at submission time.
/// </summary>
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
