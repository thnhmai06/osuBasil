using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;

namespace Basil.Infrastructure.Beatmaps.Logic;

/// <summary>Checks that a stored beatmapset has every file it serves.</summary>
internal sealed class BeatmapsetFileAudit(IBeatmapsetStorage storage, IBeatmapRepository beatmaps)
{
	/// <summary>Determines whether a stored beatmapset misses a file it serves.</summary>
	/// <param name="set">The stored beatmapset.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when its archive or any file made from it is not stored.</returns>
	public async Task<bool> MissingFilesAsync(BeatmapsetRecord set, CancellationToken ct = default)
	{
		if (await ArchiveMissingAsync(set, true, ct)) return true;
		if (await ArchiveMissingAsync(set, false, ct)) return true;

		if (set.Assets.HasStoryboard &&
		    !await storage.ExistsAsync(set, BeatmapsetFileType.Storyboard, StoredFileNames.Storyboard,
			    ct))
			return true;

		foreach (var beatmap in await beatmaps.ListAsync(set, ct))
			if (await BeatmapFilesMissingAsync(set, beatmap, ct))
				return true;

		return false;
	}

	private async Task<bool> ArchiveMissingAsync(BeatmapsetRecord set, bool withVideo,
		CancellationToken ct)
	{
		var archive = await storage.OpenArchiveAsync(set, withVideo, ct);
		if (archive is null) return true;
		await archive.DisposeAsync();
		return false;
	}

	private async Task<bool> BeatmapFilesMissingAsync(BeatmapsetRecord set, BeatmapRecord beatmap,
		CancellationToken ct)
	{
		if (!await storage.ExistsAsync(set, BeatmapsetFileType.Version, $"{beatmap.Hash}.osu", ct))
			return true;

		if (beatmap.Assets.BackgroundFile is { } background &&
		    !await storage.ExistsAsync(set, BeatmapsetFileType.Background, background, ct))
			return true;

		if (beatmap.Assets.AudioFile is { } audio && await AudioMissingAsync(set, audio, ct))
			return true;

		return beatmap.Assets.VideoFile is { } video &&
		       !await storage.ExistsAsync(set, BeatmapsetFileType.Video, video, ct);
	}

	private async Task<bool> AudioMissingAsync(BeatmapsetRecord set, string audio, CancellationToken ct)
	{
		return !await storage.ExistsAsync(set, BeatmapsetFileType.Audio, audio, ct) ||
		       !await storage.ExistsAsync(set, BeatmapsetFileType.Preview, audio, ct);
	}
}