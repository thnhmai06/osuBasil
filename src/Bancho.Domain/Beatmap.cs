namespace Bancho.Domain;

/// <summary>
/// Ported from app/objects/beatmap.py's Beatmap fields. Star rating (Diff) is a direct
/// passthrough of osu!api's difficultyrating field — bancho.py does not run a local difficulty
/// calculator here, so this record never touches IBeatmapDifficultyCalculator (that FFI is only
/// needed for Phase 7's mods-adjusted star rating in multiplayer rooms).
/// </summary>
public sealed record Beatmap(
    string Md5,
    int Id,
    int SetId,
    string Artist,
    string Title,
    string Version,
    string Creator,
    DateTime LastUpdate,
    int TotalLength,
    int MaxCombo,
    RankedStatus Status,
    bool Frozen,
    int Plays,
    int Passes,
    GameMode Mode,
    double Bpm,
    double Cs,
    double Od,
    double Ar,
    double Hp,
    double Diff,
    string Filename)
{
    /// <summary>Ported from Beatmap.full_name.</summary>
    public string FullName => $"{Artist} - {Title} [{Version}]";

    /// <summary>
    /// Ported from the literal `bmap.status &lt; RankedStatus.Ranked` gate in
    /// beatmap_leaderboards.py:119 (the getscores endpoint's actual check) — not the same set as
    /// Beatmap.has_leaderboard's (Ranked, Approved, Loved) property, which excludes Qualified.
    /// </summary>
    public bool HasLeaderboard => Status >= RankedStatus.Ranked;
}
