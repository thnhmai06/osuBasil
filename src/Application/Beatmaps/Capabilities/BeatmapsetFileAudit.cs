using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Capabilities;

/// <summary>Checks that a stored beatmapset has every file it serves.</summary>
internal sealed class BeatmapsetFileAudit(IBeatmapsetStorage storage, IBeatmapRepository beatmaps)
{
	/// <summary>Determines whether a stored beatmapset misses a file it serves.</summary>
	/// <param name="set">The stored beatmapset.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when its archive or any file made from it is not stored.</returns>
	public async Task<bool> MissingFilesAsync(BeatmapsetRecord set, CancellationToken cancellationToken)
	{
		if (await ArchiveMissingAsync(set, true, cancellationToken)) return true;
		if (await ArchiveMissingAsync(set, false, cancellationToken)) return true;

		if (set.Assets.HasStoryboard &&
		    !await storage.ExistsAsync(set, BeatmapsetFileType.Storyboard, StoredFileNames.Storyboard,
			    cancellationToken))
			return true;

		foreach (var beatmap in await beatmaps.ListAsync(set, cancellationToken))
			if (await BeatmapFilesMissingAsync(set, beatmap, cancellationToken))
				return true;

		return false;
	}

	private async Task<bool> ArchiveMissingAsync(BeatmapsetRecord set, bool withVideo,
		CancellationToken cancellationToken)
	{
		var archive = await storage.OpenArchiveAsync(set, withVideo, cancellationToken);
		if (archive is null) return true;
		await archive.DisposeAsync();
		return false;
	}

	private async Task<bool> BeatmapFilesMissingAsync(BeatmapsetRecord set, BeatmapRecord beatmap,
		CancellationToken cancellationToken)
	{
		if (!await storage.ExistsAsync(set, BeatmapsetFileType.Version, $"{beatmap.Hash}.osu", cancellationToken))
			return true;

		if (beatmap.Assets.BackgroundFile is { } background &&
		    !await storage.ExistsAsync(set, BeatmapsetFileType.Background, background, cancellationToken))
			return true;

		if (beatmap.Assets.AudioFile is { } audio && await AudioMissingAsync(set, audio, cancellationToken))
			return true;

		return beatmap.Assets.VideoFile is { } video &&
		       !await storage.ExistsAsync(set, BeatmapsetFileType.Video, video, cancellationToken);
	}

	private async Task<bool> AudioMissingAsync(BeatmapsetRecord set, string audio, CancellationToken cancellationToken)
	{
		return !await storage.ExistsAsync(set, BeatmapsetFileType.Audio, audio, cancellationToken) ||
		       !await storage.ExistsAsync(set, BeatmapsetFileType.Preview, audio, cancellationToken);
	}
}