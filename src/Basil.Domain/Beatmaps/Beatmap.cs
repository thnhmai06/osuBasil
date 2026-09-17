using System.Text.Json.Serialization;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     Represents a single difficulty within a <see cref="Beatmapset" />.
/// </summary>
/// <remarks>
///     Identified by its content hash (<see cref="Md5" />) and its osu! id. The
///     <see cref="Difficulty" /> value holds the gameplay stats, including the star rating,
///     computed by the difficulty analyzer in Basil.Infrastructure. <see cref="Filename" />, the
///     background file, and the audio file are resolved against the set's storage folder and are
///     not serialized to the wire.
/// </remarks>
public sealed class Beatmap : IEquatable<Beatmap>
{
	/// <summary>
	///     The id floor for beatmapset ingested locally without a real osu! online id.
	/// </summary>
	/// <remarks>
	///     Real osu! online ids remain well below this value, so this floor keeps collisions with
	///     locally assigned ids implausible without a dedicated id-space reservation table.
	/// </remarks>
	private const int LocalIdFloor = 1_000_000_000;

	/// <summary>The MD5 hash of the beatmap file's contents.</summary>
	public required string Md5 { get; init; }

	/// <summary>The osu! id of the beatmap.</summary>
	public required int? Id { get; init; }

	/// <summary>The set this difficulty belongs to.</summary>
	public required Beatmapset? Beatmapset { get; init; }

	/// <summary>The difficulty name, such as "Insane".</summary>
	public required string Version { get; init; }

	/// <summary>The gameplay stats of the beatmap.</summary>
	public required Difficulty Difficulty { get; init; }

	/// <summary>The per-mode hit-object counts of the beatmap.</summary>
	public required BeatmapObjects Objects { get; init; }

	/// <summary>The name of the beatmap file on disk.</summary>
	[JsonIgnore]
	public string? Filename { get; init; }

	/// <summary>
	///     The background image file name resolved against the set's storage folder, or
	///     <see langword="null" /> if the beatmap has no background.
	/// </summary>
	[JsonIgnore]
	public string? BackgroundFile { get; init; }

	/// <summary>
	///     The audio file name resolved against the set's storage folder, or
	///     <see langword="null" /> if the beatmap has no audio.
	/// </summary>
	[JsonIgnore]
	public string? AudioFile { get; init; }

	/// <summary>
	///     The audio preview time in milliseconds, or <see langword="null" /> if unknown.
	/// </summary>
	[JsonIgnore]
	public int? PreviewTime { get; init; }

	/// <summary>
	///     Gets a value that indicates whether the beatmap was ingested without a real osu! online
	///     id.
	/// </summary>
	/// <value>
	///     <see langword="true" /> if the beatmap's id is at or above <see cref="LocalIdFloor" />;
	///     otherwise, <see langword="false" />.
	/// </value>
	public bool? IsLocallyIngested => Id is not null ? Id >= LocalIdFloor : null;

	/// <summary>
	///     Gets the full display name of the beatmap.
	/// </summary>
	/// <value>An "Artist - Title [Version]" string.</value>
	public string? FullName => Beatmapset != null
		? $"{Beatmapset.Artist} - {Beatmapset.Title} [{Version}]"
		: null;

	public bool Equals(Beatmap? other)
	{
		if (other is null) return false;
		return Md5 == other.Md5;
	}

	public override bool Equals(object? obj)
	{
		return obj is Beatmap other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Md5.GetHashCode();
	}
}