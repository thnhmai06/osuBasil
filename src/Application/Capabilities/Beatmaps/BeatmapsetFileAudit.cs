using Basil.Application.Persistence.Beatmaps;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Capabilities.Beatmaps;

/// <summary>Checks that a stored beatmapset has every file it serves.</summary>
internal sealed class BeatmapsetFileAudit(IBeatmapsetStorage storage, IBeatmapRepository beatmaps)
{
	/// <summary>Determines whether a stored beatmapset misses a file it serves.</summary>
	/// <param name="set">The stored beatmapset.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when its archive or any file made from it is not stored.</returns>
	public async Task<bool> MissingFilesAsync(BeatmapsetRecord set, CancellationToken cancellationToken)
	{
		var archive = await storage.OpenArchiveAsync(set, true, cancellationToken);
		if (archive is null) return true;
		await archive.DisposeAsync();

		var withoutVideo = await storage.OpenArchiveAsync(set, false, cancellationToken);
		if (withoutVideo is null) return true;
		await withoutVideo.DisposeAsync();

		if (set.Assets.HasStoryboard &&
		    !await storage.ExistsAsync(set, BeatmapsetFileType.Storyboard, StoredFileNames.Storyboard,
			    cancellationToken))
			return true;

		foreach (var beatmap in await beatmaps.ListAsync(set, cancellationToken))
		{
			if (!await storage.ExistsAsync(set, BeatmapsetFileType.Version, $"{beatmap.Hash}.osu",
				    cancellationToken))
				return true;

			if (beatmap.Assets.BackgroundFile is { } background &&
			    !await storage.ExistsAsync(set, BeatmapsetFileType.Background, background, cancellationToken))
				return true;

			if (beatmap.Assets.AudioFile is { } audio)
			{
				if (!await storage.ExistsAsync(set, BeatmapsetFileType.Audio, audio, cancellationToken))
					return true;

				if (!await storage.ExistsAsync(set, BeatmapsetFileType.Preview, audio, cancellationToken))
					return true;
			}

			if (beatmap.Assets.VideoFile is { } video &&
			    !await storage.ExistsAsync(set, BeatmapsetFileType.Video, video, cancellationToken))
				return true;
		}

		return false;
	}
}