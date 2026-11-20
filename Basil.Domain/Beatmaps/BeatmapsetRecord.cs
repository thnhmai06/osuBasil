using Basil.Domain.Content;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Beatmaps;

/// <summary>A beatmapset, the group of difficulties of one song; two records are equal when they have the same id.</summary>
public sealed class BeatmapsetRecord : IEquatable<BeatmapsetRecord>
{
	private const int LocalIdFloor = 1_000_000_000;

	/// <summary>The identifier of the beatmapset, at least 1.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is below 1.</exception>
	public required int Id
	{
		get;
		init => field = value >= 1
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "Beatmapset ids start at 1.");
	}

	/// <summary>True when the id lies in the range reserved for sets added to this server rather than known from elsewhere.</summary>
	public bool IsLocallyIngested => Id >= LocalIdFloor;

	/// <summary>The identifier of the stored content of the set.</summary>
	public required ContentIdentifier ContentId { get; set; }

	/// <summary>The artist, title and creator of the set.</summary>
	public required BeatmapsetMetadata Metadata { get; set; }

	/// <summary>The flags that describe the content of the set.</summary>
	public required BeatmapsetAssets Assets { get; set; }

	/// <summary>The ranking state of the set.</summary>
	public BeatmapsetStatus Status { get; set; } = BeatmapsetStatus.Ranked;

	/// <summary>When the set was last changed.</summary>
	public required DateTimeOffset UpdatedAt { get; set; }

	/// <summary>When the set was first recorded.</summary>
	public required DateTimeOffset CreatedAt { get; init; }

	/// <summary>True when the set is locked, which also locks every beatmap in it.</summary>
	public bool Locked { get; set; } = false;

	/// <summary>True unless the set is hidden, which also hides every beatmap in it.</summary>
	public bool Visible { get; set; } = true;

	public bool Equals(BeatmapsetRecord? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is BeatmapsetRecord other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id;
	}
}

/// <summary>The flags that describe the content of a beatmapset.</summary>
public sealed record BeatmapsetAssets
{
	/// <summary>True when the set has a storyboard.</summary>
	public bool HasStoryboard { get; init; }
}

/// <summary>Names a beatmap by hash, id and full name, whether or not the server has its record.</summary>
/// <param name="Hash">The MD5 hash of the beatmap file.</param>
/// <param name="Id">The identifier of the beatmap.</param>
/// <param name="FullName">The name of the beatmap as the game displays it.</param>
/// <param name="Known">The record of the beatmap; null when the server has none.</param>
public readonly record struct BeatmapReference(Md5Hash Hash, int Id, string FullName, BeatmapRecord? Known);