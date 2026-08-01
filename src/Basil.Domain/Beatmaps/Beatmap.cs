using System.Text.Json.Serialization;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     Represents a single difficulty within a <see cref="Mapset" />.
/// </summary>
/// <param name="Md5">The MD5 hash of the beatmap file's contents.</param>
/// <param name="Id">The osu! id of the beatmap.</param>
/// <param name="Mapset">The set this difficulty belongs to.</param>
/// <param name="Version">The difficulty name, such as "Insane".</param>
/// <param name="Filename">The name of the beatmap file on disk.</param>
/// <param name="Difficulty">The gameplay stats of the beatmap.</param>
/// <param name="BeatmapObjectCounts">The per-mode hit-object counts of the beatmap.</param>
/// <param name="BackgroundFile">
///     The background image file name resolved against the set's storage folder, or
///     <see langword="null" /> if the beatmap has no background.
/// </param>
/// <param name="AudioFile">
///     The audio file name resolved against the set's storage folder, or
///     <see langword="null" /> if the beatmap has no audio.
/// </param>
/// <param name="PreviewTime">
///     The audio preview time in milliseconds, or <see langword="null" /> if unknown.
/// </param>
/// <remarks>
///     Identified by its content hash (<see cref="Md5" />) and its osu! id. The
///     <see cref="Difficulty" /> value holds the gameplay stats, including the star rating,
///     computed by the difficulty analyzer in Basil.Infrastructure. <see cref="Filename" />, the
///     background file, and the audio file are resolved against the set's storage folder and are
///     not serialized to the wire.
/// </remarks>
public sealed record Beatmap(

	#region Identity

	string Md5,
	int Id,
	Mapset Mapset,

	#endregion

	#region Metadata

	string Version,
	[property: JsonIgnore] string Filename,

	#endregion

	#region Stats

	Difficulty Difficulty,
	BeatmapObjectCounts BeatmapObjectCounts,

	#endregion

	#region Background

	[property: JsonIgnore] string? BackgroundFile = null,

	#endregion

	#region Audio

	[property: JsonIgnore] string? AudioFile = null,
	[property: JsonIgnore] int? PreviewTime = null

	#endregion

)
{
	/// <summary>
	///     The id floor for beatmaps ingested locally without a real osu! online id.
	/// </summary>
	/// <remarks>
	///     Real osu! online ids remain well below this value, so this floor keeps collisions with
	///     locally assigned ids implausible without a dedicated id-space reservation table.
	/// </remarks>
	public const int LocalIdFloor = 1_000_000_000;

	/// <summary>
	///     Gets a value that indicates whether the beatmap was ingested without a real osu! online
	///     id.
	/// </summary>
	/// <value>
	///     <see langword="true" /> if the beatmap's id is at or above <see cref="LocalIdFloor" />;
	///     otherwise, <see langword="false" />.
	/// </value>
	public bool IsLocallyIngested => Id >= LocalIdFloor;

	/// <summary>
	///     Gets the full display name of the beatmap.
	/// </summary>
	/// <value>An "Artist - Title [Version]" string.</value>
	public string FullName => $"{Mapset.Artist} - {Mapset.Title} [{Version}]";
}