namespace Basil.Domain.Beatmaps;

/// <summary>The files of its set a difficulty uses, each by the name it is stored under.</summary>
public sealed record BeatmapAssets
{
	/// <summary>Whether the difficulty draws a storyboard of its own.</summary>
	public bool HasStoryboard { get; init; }

	/// <summary>The name of the difficulty's background in the set's backgrounds, or <see langword="null" /> when it has none.</summary>
	public string? BackgroundFile { get; init; }

	/// <summary>The name of the difficulty's audio track in the set's audio, or <see langword="null" /> when it has none.</summary>
	public string? AudioFile { get; init; }

	/// <summary>The name of the difficulty's video in the set's videos, or <see langword="null" /> when it has none.</summary>
	public string? VideoFile { get; init; }

	/// <summary>Where the audio preview starts, or <see langword="null" /> when the difficulty sets none.</summary>
	public TimeSpan? PreviewTime { get; init; }
}
