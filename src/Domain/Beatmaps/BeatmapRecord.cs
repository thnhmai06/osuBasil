using Basil.Domain.Common;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     A stored beatmap difficulty identified by its id.
/// </summary>
public sealed class BeatmapRecord : IEquatable<BeatmapRecord>
{
	/// <summary>
	///     The lowest id of a beatmap that has no osu! id and was given a local one.
	/// </summary>
	/// <remarks>Real osu! ids stay well below this value.</remarks>
	private const int LocalIdFloor = 1_000_000_000;

	/// <summary>Gets the unique identifier of the beatmap: its osu! id, or a local id.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
	public required int Id
	{
		get;
		init => field = value >= 1
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "Beatmap ids start at 1.");
	}

	/// <summary>
	///     Gets a value that indicates whether the beatmap has no osu! id and was given a local one.
	/// </summary>
	/// <value>
	///     <see langword="true" /> if the beatmap's id is at or above <see cref="LocalIdFloor" />;
	///     otherwise, <see langword="false" />.
	/// </value>
	public bool IsLocallyIngested => Id >= LocalIdFloor;

	/// <summary>Gets or sets the beatmapset the difficulty belongs to.</summary>
	public required BeatmapsetRecord Beatmapset { get; set; }

	/// <summary>Gets or sets the MD5 hash of the difficulty's .osu file, which identifies its content.</summary>
	public required Md5 Hash { get; set; }

	/// <summary>Gets or sets what the difficulty is: its name, gameplay stats and hit objects.</summary>
	public required BeatmapMetadata Metadata { get; set; }

	/// <summary>Gets or sets the files of the set the difficulty uses.</summary>
	public required BeatmapAssets Assets { get; set; }

	/// <summary>
	///     Whether the difficulty is write-locked. Locked difficulties cannot be updated or deleted.
	/// </summary>
	public bool Locked { get; set; } = false;

	/// <summary>
	///     Whether the difficulty is shown in public listings and on the public beatmap endpoints.
	/// </summary>
	public bool Visible { get; set; } = true;

	/// <summary>The time of the latest import of the difficulty, in UTC.</summary>
	public required DateTimeOffset UpdatedAt { get; set; }

	/// <summary>The time the difficulty was first imported, in UTC.</summary>
	public required DateTimeOffset CreatedAt { get; init; }

	/// <summary>
	///     Gets the full display name of the beatmap.
	/// </summary>
	/// <value>An "Artist - Title [Version]" string.</value>
	public string Name => $"{Beatmapset.Metadata.Name} [{Metadata.Version}]";

	/// <summary>
	///     Determines whether another beatmap refers to the same difficulty.
	/// </summary>
	/// <param name="other">The beatmap to compare, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> is non-null and has the same
	///     <see cref="Id" /> as this beatmap; otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(BeatmapRecord? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	/// <summary>Determines whether the difficulty or its set is locked.</summary>
	/// <returns><see langword="true" /> if either is locked; otherwise, <see langword="false" />.</returns>
	public bool IsLocked()
	{
		return Locked || Beatmapset.Locked;
	}

	/// <summary>Determines whether both the difficulty and its set are shown.</summary>
	/// <returns><see langword="true" /> if both are visible; otherwise, <see langword="false" />.</returns>
	public bool IsVisible()
	{
		return Visible && Beatmapset.Visible;
	}

	/// <summary>
	///     Determines whether this beatmap equals another object.
	/// </summary>
	/// <param name="obj">The object to compare, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="BeatmapRecord" /> that equals
	///     this one; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is BeatmapRecord other && Equals(other);
	}

	/// <summary>Returns the hash code of this beatmap.</summary>
	/// <returns>The <see cref="Id" />, which uniquely identifies the beatmap.</returns>
	public override int GetHashCode()
	{
		return Id;
	}
}