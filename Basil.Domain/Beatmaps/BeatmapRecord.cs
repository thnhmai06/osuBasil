using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Beatmaps;

public sealed class BeatmapRecord : IEquatable<BeatmapRecord>
{
	private const int LocalIdFloor = 1_000_000_000;

	public required int Id
	{
		get;
		init => field = value >= 1
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "Beatmap ids start at 1.");
	}

	public bool IsLocallyIngested => Id >= LocalIdFloor;

	public required Md5Hash Hash { get; set; }

	public required BeatmapsetRecord Beatmapset { get; set; }

	public required BeatmapMetadata Metadata { get; set; }

	public required BeatmapAssets Assets { get; set; }

	public bool Locked { get; set; } = false;

	public bool Visible { get; set; } = true;

	public required DateTimeOffset UpdatedAt { get; set; }

	public required DateTimeOffset CreatedAt { get; init; }

	public string FullName => $"{Beatmapset.Metadata.FullName} [{Metadata.VersionName}]";

	public BeatmapReference Reference => new(Hash, Id, FullName, this);

	public string FileName
	{
		get
		{
			const string invalidOsuFileNameCharacters = ":\\/*<>?\"|";
			var rawFileName = $"{FullName}.osu";

			return string.Concat(rawFileName.Where(character => invalidOsuFileNameCharacters.IndexOf(character) < 0));
		}
	}

	public bool IsLocked()
	{
		return Locked || Beatmapset.Locked;
	}

	public bool IsVisible()
	{
		return Visible && Beatmapset.Visible;
	}

	public bool Equals(BeatmapRecord? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is BeatmapRecord other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id;
	}
}

public sealed record BeatmapAssets
{
	public bool HasStoryboard { get; init; }

	public string? BackgroundFile { get; init; }

	public string? AudioFile { get; init; }

	public string? VideoFile { get; init; }

	public TimeSpan? PreviewTime { get; init; }
}