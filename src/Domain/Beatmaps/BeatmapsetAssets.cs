namespace Basil.Domain.Beatmaps;

/// <summary>The files a beatmapset has besides its difficulties.</summary>
public sealed record BeatmapsetAssets
{
	/// <summary>Whether the set has a storyboard of its own, shared by every difficulty.</summary>
	public bool HasStoryboard { get; init; }
}