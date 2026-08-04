namespace Basil.Domain.Beatmaps;

/// <summary>
///     Represents a beatmapset, the shared metadata for a group of beatmap difficulties.
/// </summary>
/// <param name="Id">The unique identifier of the set.</param>
/// <param name="Artist">The artist of the set's music.</param>
/// <param name="Title">The title of the set's music.</param>
/// <param name="Creator">The username of the set's creator.</param>
/// <param name="LastUpdate">The time of the latest re-ingestion or content change, in UTC.</param>
/// <param name="CreatedAt">The time the set was first ingested, in UTC.</param>
/// <param name="IsFrozen">
///     Whether the set is write-locked by an admin. Frozen sets cannot be updated or deleted.
/// </param>
/// <param name="IsPrivate">
///     Whether the set is hidden from non-admin listings and from the public beatmap endpoints.
/// </param>
/// <param name="BackgroundFile">
///     The background image file name resolved against the set's storage folder, or
///     <see langword="null" /> if the set has no background.
/// </param>
/// <param name="AudioFile">
///     The audio file name resolved against the set's storage folder, or
///     <see langword="null" /> if the set has no audio.
/// </param>
/// <remarks>
///     Artist, Title, Creator, and LastUpdate are shared by every difficulty in the set, so they
///     live here instead of being duplicated on each <see cref="Beatmap" />.
///     <see cref="CreatedAt" /> records the first ingestion time, distinct from
///     <see cref="LastUpdate" />, which changes on every re-ingestion or content change.
///     <see cref="BackgroundFile" /> is the lowest-id beatmap's
///     <see cref="Beatmap.BackgroundFile" /> in the set, kept in sync by ingestion. It backs the
///     per-set thumbnail on the b.&lt;domain&gt; host and the set-level background route on the
///     api. host, so neither has to scan every beatmap in the set per request.
/// </remarks>
public sealed record Beatmapset(
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
	///     Gets the ranked status of the set.
	/// </summary>
	/// <value>
	///     Always <see cref="BeatmapStatus.Approved" />. Every beatmap in the server's database is
	///     treated as loved; Basil does not track per-map ranked-status curation.
	/// </value>
	public static BeatmapStatus Status => BeatmapStatus.Approved;

	/// <summary>
	///     Gets a value that indicates whether the set was ingested without a real osu! online id.
	/// </summary>
	/// <value>
	///     <see langword="true" /> if the set's id is at or above <see cref="Beatmap.LocalIdFloor" />;
	///     otherwise, <see langword="false" />.
	/// </value>
	public bool IsLocallyIngested => Id >= Beatmap.LocalIdFloor;
}