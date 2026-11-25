using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Beatmaps;

/// <summary>One difficulty of a beatmapset; two records are equal when they have the same id.</summary>
public sealed class BeatmapRecord : IEquatable<BeatmapRecord>
{
	private const int LocalIdFloor = 1_000_000_000;

	/// <summary>The identifier of the beatmap, at least 1.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is below 1.</exception>
	public required int Id
	{
		get;
		init => field = value >= 1
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "Beatmap ids start at 1.");
	}

	/// <summary>True when the id lies in the range reserved for beatmaps added to this server rather than known from elsewhere.</summary>
	public bool IsLocallyIngested => Id >= LocalIdFloor;

	/// <summary>The MD5 hash of the beatmap file, used to recognise the beatmap.</summary>
	public required Md5Hash Hash { get; set; }

	/// <summary>The set this beatmap belongs to.</summary>
	public required BeatmapsetRecord Beatmapset { get; set; }

	/// <summary>The difficulty name, difficulty values and object counts.</summary>
	public required BeatmapMetadata Metadata { get; set; }

	/// <summary>The files and flags that describe the content of the beatmap.</summary>
	public required BeatmapAssets Assets { get; set; }

	/// <summary>True when the beatmap itself is locked; see <see cref="IsLocked"/> for the combined state.</summary>
	public bool Locked { get; set; } = false;

	/// <summary>True unless the beatmap itself is hidden; see <see cref="IsVisible"/> for the combined state.</summary>
	public bool Visible { get; set; } = true;

	/// <summary>When the beatmap was last changed.</summary>
	public required DateTimeOffset UpdatedAt { get; set; }

	/// <summary>When the beatmap was first recorded.</summary>
	public required DateTimeOffset CreatedAt { get; init; }

	/// <summary>The set's full name followed by the difficulty name in brackets, as the game displays it.</summary>
	public string FullName => $"{Beatmapset.Metadata.FullName} [{Metadata.VersionName}]";

	/// <summary>A reference to this beatmap that names it by hash, id and full name.</summary>
	public BeatmapReference Reference => new(Hash, Id, FullName, this);

	/// <summary>The name of the beatmap's .osu file: the full name plus the extension, without the characters a file name cannot hold.</summary>
	public string FileName
	{
		get
		{
			const string invalidOsuFileNameCharacters = ":\\/*<>?\"|";
			var rawFileName = $"{FullName}.osu";

			return string.Concat(rawFileName.Where(character => invalidOsuFileNameCharacters.IndexOf(character) < 0));
		}
	}

	/// <summary>Tells whether the beatmap or its set is locked.</summary>
	public bool IsLocked()
	{
		return Locked || Beatmapset.Locked;
	}

	/// <summary>Tells whether both the beatmap and its set are visible.</summary>
	public bool IsVisible()
	{
		return Visible && Beatmapset.Visible;
	}

	/// <summary>Tells whether a user holding these effective privileges may see the beatmap: it is visible, or the user may see hidden ones.</summary>
	public bool CanBeViewedBy(UserPrivileges effective)
	{
		return IsVisible() || BeatmapsetRecord.MayViewHidden(effective);
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

/// <summary>The files and flags that describe the content of a beatmap.</summary>
public sealed record BeatmapAssets
{
	/// <summary>True when the beatmap has its own storyboard.</summary>
	public bool HasStoryboard { get; init; }

	/// <summary>The name of the background image file; null when there is none.</summary>
	public string? BackgroundFile { get; init; }

	/// <summary>The name of the audio file; null when there is none.</summary>
	public string? AudioFile { get; init; }

	/// <summary>The name of the background video file; null when there is none.</summary>
	public string? VideoFile { get; init; }

	/// <summary>Where the song preview starts; null when the beatmap sets none.</summary>
	public TimeSpan? PreviewTime { get; init; }
}