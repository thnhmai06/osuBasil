using Basil.Domain.Utilities;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     A stored beatmap difficulty identified by its id.
/// </summary>
public sealed class Beatmap : IWrapper<BeatmapData>, IEquatable<Beatmap>
{
	/// <summary>
	///     The lowest id of a beatmap that has no osu! id and was given a local one.
	/// </summary>
	/// <remarks>Real osu! ids stay well below this value.</remarks>
	public const int LocalIdFloor = 1_000_000_000;

	/// <summary>Gets the unique identifier of the beatmap: its osu! id, or a local id.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
	public required int Id
	{
		get;
		init => field = value >= 1
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "Beatmap ids start at 1.");
	}

	/// <summary>Gets or sets the beatmap data this identity wraps.</summary>
	public required BeatmapData Value { get; set; }

	/// <summary>
	///     Gets a value that indicates whether the beatmap has no osu! id and was given a local one.
	/// </summary>
	/// <value>
	///     <see langword="true" /> if the beatmap's id is at or above <see cref="LocalIdFloor" />;
	///     otherwise, <see langword="false" />.
	/// </value>
	public bool IsLocallyIngested => Id >= LocalIdFloor;

	/// <summary>
	///     Determines whether another beatmap refers to the same difficulty.
	/// </summary>
	/// <param name="other">The beatmap to compare, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> is non-null and has the same
	///     <see cref="Id" /> as this beatmap; otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(Beatmap? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	/// <summary>
	///     Determines whether this beatmap equals another object.
	/// </summary>
	/// <param name="obj">The object to compare, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="Beatmap" /> that equals
	///     this one; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is Beatmap other && Equals(other);
	}

	/// <summary>Returns the hash code of this beatmap.</summary>
	/// <returns>The <see cref="Id" />, which uniquely identifies the beatmap.</returns>
	public override int GetHashCode()
	{
		return Id;
	}
}

/// <summary>
///     A single difficulty within a <see cref="Beatmapset" />.
/// </summary>
/// <remarks>
///     The <see cref="Difficulty" /> value holds the gameplay stats, including the star rating.
/// </remarks>
public sealed class BeatmapData
{
	/// <summary>The MD5 hash of the beatmap file's contents.</summary>
	public required Md5 Hash { get; init; }

	/// <summary>The set this difficulty belongs to.</summary>
	public required Beatmapset Beatmapset { get; init; }

	/// <summary>The difficulty name, such as "Insane".</summary>
	public required string Version { get; init; } = string.Empty;

	/// <summary>The gameplay stats of the beatmap.</summary>
	public required Difficulty Difficulty { get; init; }

	/// <summary>The per-mode hit-object counts of the beatmap.</summary>
	public required BeatmapObjects Objects { get; init; }

	/// <summary>
	///     Whether the difficulty is write-locked. Locked difficulties cannot be updated or deleted.
	/// </summary>
	public bool Locked { get; set; } = false;

	/// <summary>
	///     Whether the difficulty is shown in public listings and on the public beatmap endpoints.
	/// </summary>
	public bool Visible { get; set; } = true;

	/// <summary>
	///     Gets the full display name of the beatmap.
	/// </summary>
	/// <value>An "Artist - Title [Version]" string.</value>
	public string FullName => $"{Beatmapset.Value.Artist} - {Beatmapset.Value.Title} [{Version}]";

	/// <summary>Determines whether the difficulty or its set is locked.</summary>
	/// <returns><see langword="true" /> if either is locked; otherwise, <see langword="false" />.</returns>
	public bool IsLocked()
	{
		return Locked || Beatmapset.Value.Locked;
	}

	/// <summary>Determines whether both the difficulty and its set are shown.</summary>
	/// <returns><see langword="true" /> if both are visible; otherwise, <see langword="false" />.</returns>
	public bool IsVisible()
	{
		return Visible && Beatmapset.Value.Visible;
	}
}