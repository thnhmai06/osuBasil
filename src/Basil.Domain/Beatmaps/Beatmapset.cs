using System.Text.Json.Serialization;
using Basil.Domain.Users;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     Represents a beatmapset, the shared metadata for a group of beatmap difficulties.
/// </summary>
/// <remarks>
///     Artist, Title, Creator, and LastUpdate are shared by every difficulty in the set, so they
///     live here instead of being duplicated on each <see cref="Beatmap" />.
///     <see cref="CreatedAt" /> records the first ingestion time, distinct from
///     <see cref="LastUpdate" />, which changes on every re-ingestion or content change.
///     <see cref="BackgroundFile" /> is the lowest-id beatmap's
///     <see cref="Beatmap.BackgroundFile" /> in the set. It backs the per-set thumbnail on the
///     b.&lt;domain&gt; host and the set-level background route on the api. host.
/// </remarks>
public sealed class Beatmapset : IEquatable<Beatmapset>
{
	/// <summary>
	///     The id floor for beatmapset ingested locally without a real osu! online id.
	/// </summary>
	/// <remarks>
	///     Real osu! online ids remain well below this value, so this floor keeps collisions with
	///     locally assigned ids implausible without a dedicated id-space reservation table.
	/// </remarks>
	private const int LocalIdFloor = 1_000_000_000;

	/// <summary>The unique identifier of the set.</summary>
	public required int Id { get; init; }

	/// <summary>
	///     Gets the ranked status of the set.
	/// </summary>
	public static BeatmapStatus Status => BeatmapStatus.Approved;

	/// <summary>The artist of the set's music.</summary>
	public required string Artist { get; set; }

	/// <summary>The title of the set's music.</summary>
	public required string Title { get; set; }

	/// <summary>The username of the set's creator.</summary>
	public required User Creator { get; init; }

	/// <summary>The time of the latest re-ingestion or content change, in UTC.</summary>
	public required DateTimeOffset LastUpdate { get; set; }

	/// <summary>The time the set was first ingested, in UTC.</summary>
	public required DateTimeOffset CreatedAt { get; init; }

	/// <summary>
	///     Whether the set is write-locked by an admin. Frozen sets cannot be updated or deleted.
	/// </summary>
	public required bool IsLocked { get; set; }

	/// <summary>
	///     Whether the set is hidden from non-admin listings and from the public beatmap endpoints.
	/// </summary>
	public required bool IsVisible { get; set; }

	/// <summary>
	///     Gets a value that indicates whether the set was ingested without a real osu! online id.
	/// </summary>
	/// <value>
	///     <see langword="true" /> if the set's id is at or above <see cref="Beatmap.LocalIdFloor" />;
	///     otherwise, <see langword="false" />.
	/// </value>
	public bool IsLocallyIngested => Id >= LocalIdFloor;

	/// <summary>
	///     The background image file name resolved against the set's storage folder, or
	///     <see langword="null" /> if the set has no background.
	/// </summary>
	[JsonIgnore]
	public string? BackgroundFile { get; set; }

	/// <summary>
	///     The audio file name resolved against the set's storage folder, or
	///     <see langword="null" /> if the set has no audio.
	/// </summary>
	[JsonIgnore]
	public string? AudioFile { get; set; }

	public bool Equals(Beatmapset? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is Beatmapset other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id;
	}
}