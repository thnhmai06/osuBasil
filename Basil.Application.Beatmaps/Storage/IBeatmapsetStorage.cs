using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Storage;

public interface IBeatmapsetStorage
{
	Task<BeatmapsetArchive> SaveAsync(Stream archive, CancellationToken ct = default);

	Task SaveAsync(BeatmapsetRecord set, BeatmapsetArchive archive);

	Task<BeatmapsetArchive?> PackAsync(BeatmapsetFolder folder,
		CancellationToken ct = default);

	Task ClearAsync(BeatmapsetRecord set);

	Task SaveAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name, Stream content);

	Task SaveArchiveWithoutVideoAsync(BeatmapsetRecord set, IReadOnlyCollection<string> videoFiles);

	Task<bool> ExistsAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name,
		CancellationToken ct = default);

	Task<Stream?> OpenAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name,
		CancellationToken ct = default);

	Task<Stream?> OpenCoverAsync(BeatmapsetRecord set, string backgroundName, BeatmapsetCoverVariant variant,
		CancellationToken ct = default);

	Task<Stream?> OpenArchiveAsync(BeatmapsetRecord set, bool withVideo, CancellationToken ct = default);

	Task<IEnumerable<BeatmapsetSource>> ListAsync(CancellationToken ct = default);

	IAsyncEnumerable<BeatmapsetSource> WatchAsync(CancellationToken ct);

	Task DeleteAsync(BeatmapsetRecord set);

	Task DeleteAsync(BeatmapsetArchive archive);
}

/// <summary>The fixed picture sizes a beatmapset's background is served in, each cropped to fill its size.</summary>
public enum BeatmapsetCoverVariant : byte
{
	Cover,

	Card,

	List,

	SlimCover,

	Thumbnail,

	ThumbnailLarge
}

public enum BeatmapsetFileType : byte
{
	Version,

	Background,

	Audio,

	Preview,

	Video,

	Storyboard
}