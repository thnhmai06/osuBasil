using Basil.Domain.Content;
using Basil.Domain.Mechanics;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Beatmaps;

public sealed class BeatmapsetRecord : IEquatable<BeatmapsetRecord>
{
	private const int LocalIdFloor = 1_000_000_000;

	public required int Id
	{
		get;
		init => field = value >= 1
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "Beatmapset ids start at 1.");
	}

	public bool IsLocallyIngested => Id >= LocalIdFloor;

	public required ContentIdentifier Content { get; set; }

	public required BeatmapsetMetadata Metadata { get; set; }

	public required BeatmapsetAssets Assets { get; set; }

	public BeatmapsetStatus Status { get; set; } = BeatmapsetStatus.Ranked;

	public required DateTimeOffset UpdatedAt { get; set; }

	public required DateTimeOffset CreatedAt { get; init; }

	public bool Locked { get; set; } = false;

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

public sealed record BeatmapsetAssets
{
	public bool HasStoryboard { get; init; }
}

public readonly record struct BeatmapReference(Md5Hash Hash, int Id, string FullName, BeatmapRecord? Known);