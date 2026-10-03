using Basil.Domain.Beatmaps;

namespace Basil.Application.Contracts.Beatmaps;

/// <summary>Opens the files that come with beatmaps and beatmapsets.</summary>
public interface IBeatmapAssets
{
	/// <summary>Opens a file of a beatmap.</summary>
	/// <param name="beatmap">The beatmap whose file to open.</param>
	/// <param name="asset">The file to open.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The file's content, or <see langword="null" /> when the beatmap has no such file.</returns>
	Task<Stream?> OpenAsync(Beatmap beatmap, BeatmapAsset asset, CancellationToken cancellationToken = default);

	/// <summary>Opens a file of a beatmapset.</summary>
	/// <param name="set">The beatmapset whose file to open.</param>
	/// <param name="asset">The file to open.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The file's content, or <see langword="null" /> when the set has no such file.</returns>
	Task<Stream?> OpenAsync(Beatmapset set, BeatmapsetAsset asset, CancellationToken cancellationToken = default);
}

/// <summary>The files of one beatmap.</summary>
public enum BeatmapAsset : byte
{
	/// <summary>The difficulty file.</summary>
	File,

	/// <summary>The background image.</summary>
	Background,

	/// <summary>The audio track.</summary>
	Audio,

	/// <summary>The video.</summary>
	Video
}

/// <summary>The files of a beatmapset.</summary>
public enum BeatmapsetAsset : byte
{
	/// <summary>The background image of the set.</summary>
	Background,

	/// <summary>The audio track of the set.</summary>
	Audio,

	/// <summary>A short preview of the audio track.</summary>
	AudioPreview,

	/// <summary>The storyboard.</summary>
	Storyboard,

	/// <summary>The archive with every file.</summary>
	Archive,

	/// <summary>The archive without video files.</summary>
	ArchiveWithoutVideo
}