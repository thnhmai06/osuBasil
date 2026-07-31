namespace Basil.Domain.Beatmaps;

/// <summary>
///     One row per beatmapset — Artist/Title/Creator/Status/LastUpdate are shared by every
///     difficulty in the set, so they live here instead of being duplicated onto each
///     <see cref="Beatmap" />. CreatedAt is the first-ingestion time, distinct from LastUpdate
///     which bumps on every re-ingestion/content change. BackgroundFile is the lowest-id
///     beatmap's <see cref="Beatmap.BackgroundFile" /> in this set, kept in sync by ingestion and
///     resolved relative to this mapset's own folder (same convention as
///     <see cref="Beatmap.BackgroundFile" />) — backs b.&lt;domain&gt;'s per-set thumbnail and the
///     `api.` host's set-level background route, so neither has to scan every beatmap in the set
///     per request.
/// </summary>
public sealed record Mapset(
	int Id,
	string Artist,
	string Title,
	string Creator,
	DateTime LastUpdate,
	DateTime CreatedAt,
	bool IsFrozen = false,
	bool IsPrivate = false,
	string? BackgroundFile = null,
	string? AudioFile = null)
{
	/// <summary>
	///     Every beatmap present in this server's DB is always Loved — Basil doesn't track per-map ranked-status
	///     curation.
	/// </summary>
	public RankedStatus Status => RankedStatus.Loved;
}