namespace Basil.Domain.Beatmaps;

/// <summary>
///     One row per beatmapset — Artist/Title/Creator/Status/LastUpdate are shared by every
///     difficulty in the set, so they live here instead of being duplicated onto each
///     <see cref="Beatmap" />. CreatedAt is the first-ingestion time, distinct from LastUpdate
///     which bumps on every re-ingestion/content change.
/// </summary>
public sealed record Mapset(
    int Id,
    string Artist,
    string Title,
    string Creator,
    DateTime LastUpdate,
    DateTime CreatedAt,
    bool IsFrozen = false)
{
    /// <summary>Every beatmap present in this server's DB is always Loved — Basil doesn't track per-map ranked-status curation.</summary>
    public RankedStatus Status => RankedStatus.Loved;
}
