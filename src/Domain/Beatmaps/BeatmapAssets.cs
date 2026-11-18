namespace Basil.Domain.Beatmaps;

public sealed record BeatmapAssets
{
	public bool HasStoryboard { get; init; }

	public string? BackgroundFile { get; init; }

	public string? AudioFile { get; init; }

	public string? VideoFile { get; init; }

	public TimeSpan? PreviewTime { get; init; }
}