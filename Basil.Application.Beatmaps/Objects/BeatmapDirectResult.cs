namespace Basil.Application.Beatmaps.Objects;

/// <summary>The outcome of searching or downloading beatmapsets without the website.</summary>
public enum BeatmapDirectResult : byte
{
	/// <summary>The actor is not a supporter.</summary>
	Denied = 0,

	Ok = 1
}