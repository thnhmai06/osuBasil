using Basil.Application.Storage.Beatmaps;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Services.Beatmaps;

/// <summary>Makes the files a stored beatmapset serves from its archive.</summary>
internal sealed class BeatmapsetFileWriter(
	IBeatmapsetReader reader,
	IBeatmapsetStorage storage,
	IBeatmapRepository beatmaps,
	IBeatmapAssets assets)
{
	/// <summary>Makes every file a set serves from its archive, which is already stored.</summary>
	/// <param name="set">The beatmapset, whose archive is stored.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>The names are chosen the same way they were when the set was planned, so they match the records.</remarks>
	internal async Task StoreFilesAsync(BeatmapsetRecord set, CancellationToken cancellationToken)
	{
		await storage.ClearAsync(set, cancellationToken).ConfigureAwait(false);

		var stream = await storage.OpenArchiveAsync(set, true, cancellationToken).ConfigureAwait(false);
		if (stream is null) return;

		var keep = false;
		try
		{
			var content = await reader.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
			keep = content is not null;
			if (content is null) return;

			await using (stream.ConfigureAwait(false))
			await using (content.ConfigureAwait(false))
			{
				var backgrounds =
					StoredFileNames.ChooseNames(content.Difficulties.Select(difficulty => difficulty.Background));
				var audio = StoredFileNames.ChooseNames(content.Difficulties.Select(difficulty => difficulty.Audio));
				var video = StoredFileNames.ChooseNames(content.Difficulties.Select(difficulty => difficulty.Video));

				foreach (var difficulty in content.Difficulties)
				{
					var hash = await difficulty.File.HashAsync(cancellationToken).ConfigureAwait(false);
					var osu = await difficulty.File.OpenAsync(cancellationToken).ConfigureAwait(false);
					await using (osu.ConfigureAwait(false))
					{
						await storage.SaveAsync(set, BeatmapsetFileType.Version, $"{hash}.osu", osu, cancellationToken)
							.ConfigureAwait(false);
					}
				}

				var done = new HashSet<BeatmapsetContentFile>();
				foreach (var difficulty in content.Difficulties)
				{
					await SaveFileAsync(set, BeatmapsetFileType.Background, difficulty.Background, backgrounds, done,
						cancellationToken).ConfigureAwait(false);
					await SaveFileAsync(set, BeatmapsetFileType.Audio, difficulty.Audio, audio, done,
						cancellationToken).ConfigureAwait(false);
					await SaveFileAsync(set, BeatmapsetFileType.Video, difficulty.Video, video, done,
						cancellationToken).ConfigureAwait(false);
				}

				if (content.Storyboard is not null)
				{
					var storyboard = await content.Storyboard.OpenAsync(cancellationToken).ConfigureAwait(false);
					await using (storyboard.ConfigureAwait(false))
					{
						await storage.SaveAsync(set, BeatmapsetFileType.Storyboard, StoredFileNames.Storyboard,
							storyboard,
							cancellationToken).ConfigureAwait(false);
					}
				}
			}
		}
		finally
		{
			if (!keep) await stream.DisposeAsync().ConfigureAwait(false);
		}

		await storage.SaveArchiveWithoutVideoAsync(set, cancellationToken).ConfigureAwait(false);
		var storedBeatmaps = await beatmaps.ListAsync(set, cancellationToken).ConfigureAwait(false);
		await assets.BuildAsync(set, storedBeatmaps, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Stores one file of a set, once, under the name chosen for it.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="type">What the file is.</param>
	/// <param name="file">The file of the set's archive, or <see langword="null" /> when the set has none of that kind.</param>
	/// <param name="names">The name chosen for each file.</param>
	/// <param name="done">The files already stored by this call; <paramref name="file" /> is added to it.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private async Task SaveFileAsync(BeatmapsetRecord set, BeatmapsetFileType type, BeatmapsetContentFile? file,
		IReadOnlyDictionary<BeatmapsetContentFile, string> names, HashSet<BeatmapsetContentFile> done,
		CancellationToken cancellationToken)
	{
		if (file is null || !done.Add(file)) return;

		var stream = await file.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using (stream.ConfigureAwait(false))
		{
			await storage.SaveAsync(set, type, names[file], stream, cancellationToken).ConfigureAwait(false);
		}
	}
}