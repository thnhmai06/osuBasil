using Basil.Domain.Common;
using Basil.Domain.Content;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     A stored beatmapset identified by its id.
/// </summary>
public sealed class Beatmapset : IEquatable<Beatmapset>
{
	/// <summary>
	///     The lowest id of a beatmapset that has no osu! id and was given a local one.
	/// </summary>
	/// <remarks>Real osu! ids stay well below this value.</remarks>
	private const int LocalIdFloor = 1_000_000_000;

	/// <summary>Gets the unique identifier of the set: its osu! id, or a local id.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
	public required int Id
	{
		get;
		init => field = value >= 1
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "Beatmapset ids start at 1.");
	}

	/// <summary>
	///     Gets a value that indicates whether the set has no osu! id and was given a local one.
	/// </summary>
	/// <value>
	///     <see langword="true" /> if the set's id is at or above <see cref="LocalIdFloor" />; otherwise,
	///     <see langword="false" />.
	/// </value>
	public bool IsLocallyIngested => Id >= LocalIdFloor;

	/// <summary>Gets or sets the fingerprint of the set's archive as it was last indexed.</summary>
	public required ContentFingerprint Fingerprint { get; set; }

	/// <summary>Gets or sets what the set's difficulties share: artist, title and mapper.</summary>
	public required BeatmapsetMetadata Metadata { get; set; }

	/// <summary>Gets or sets the files the set has besides its difficulties.</summary>
	public required BeatmapsetAssets Assets { get; set; }

	/// <summary>
	///     Gets the ranked status of the set.
	/// </summary>
	/// <remarks>
	///     This member is static because Basil reports every beatmapset as approved; the status is
	///     not stored per set.
	/// </remarks>
	public static BeatmapsetStatus Status => BeatmapsetStatus.Approved;

	/// <summary>The time of the latest import of the set, in UTC.</summary>
	public required DateTimeOffset UpdatedAt { get; set; }

	/// <summary>The time the set was first imported, in UTC.</summary>
	public required DateTimeOffset CreatedAt { get; init; }

	/// <summary>
	///     Whether the set is write-locked. Locked sets cannot be updated or deleted.
	/// </summary>
	public bool Locked { get; set; } = false;

	/// <summary>
	///     Whether the set is shown in public listings and on the public beatmap endpoints.
	/// </summary>
	public bool Visible { get; set; } = true;

	/// <summary>
	///     Determines whether another beatmapset refers to the same set.
	/// </summary>
	/// <remarks>
	///     Two sets are considered equal when their <see cref="Id" /> values are equal.
	/// </remarks>
	/// <param name="other">The set to compare against, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> has the same <see cref="Id" />;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(Beatmapset? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	/// <summary>
	///     Determines whether this beatmapset equals another object.
	/// </summary>
	/// <param name="obj">The object to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="Beatmapset" /> with
	///     the same <see cref="Id" />; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is Beatmapset other && Equals(other);
	}

	/// <summary>
	///     Returns the hash code of this beatmapset.
	/// </summary>
	/// <returns>The <see cref="Id" />, which uniquely identifies the set.</returns>
	public override int GetHashCode()
	{
		return Id;
	}
}