using Basil.Application.Persistence.Beatmaps;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Capabilities.Beatmaps;

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
		await storage.ClearAsync(set, cancellationToken);

		var stream = await storage.OpenArchiveAsync(set, true, cancellationToken);
		if (stream is null) return;

		var keep = false;
		List<string> videoFiles = [];
		try
		{
			var content = await reader.ReadAsync(stream, cancellationToken);
			keep = content is not null;
			if (content is null) return;

			await using (stream)
			await using (content)
			{
				var backgrounds =
					StoredFileNames.ChooseNames(content.Difficulties.Select(difficulty => difficulty.Background));
				var audio = StoredFileNames.ChooseNames(content.Difficulties.Select(difficulty => difficulty.Audio));
				var video = StoredFileNames.ChooseNames(content.Difficulties.Select(difficulty => difficulty.Video));
				videoFiles = [.. video.Keys.Select(file => file.Name)];

				foreach (var difficulty in content.Difficulties)
				{
					var hash = await difficulty.File.HashAsync(cancellationToken);
					var osu = await difficulty.File.OpenAsync(cancellationToken);
					await using (osu)
					{
						await storage.SaveAsync(set, BeatmapsetFileType.Version, $"{hash}.osu", osu, cancellationToken);
					}
				}

				var done = new HashSet<BeatmapsetContentFile>();
				foreach (var difficulty in content.Difficulties)
				{
					await SaveFileAsync(set, BeatmapsetFileType.Background, difficulty.Background, backgrounds, done,
						cancellationToken);
					await SaveFileAsync(set, BeatmapsetFileType.Audio, difficulty.Audio, audio, done,
						cancellationToken);
					await SaveFileAsync(set, BeatmapsetFileType.Video, difficulty.Video, video, done,
						cancellationToken);
				}

				if (content.Storyboard is not null)
				{
					var storyboard = await content.Storyboard.OpenAsync(cancellationToken);
					await using (storyboard)
					{
						await storage.SaveAsync(set, BeatmapsetFileType.Storyboard, StoredFileNames.Storyboard,
							storyboard,
							cancellationToken);
					}
				}
			}
		}
		finally
		{
			if (!keep) await stream.DisposeAsync();
		}

		await storage.SaveArchiveWithoutVideoAsync(set, videoFiles, cancellationToken);
		var storedBeatmaps = await beatmaps.ListAsync(set, cancellationToken);
		await assets.BuildAsync(set, storedBeatmaps, cancellationToken);
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

		var stream = await file.OpenAsync(cancellationToken);
		await using (stream)
		{
			await storage.SaveAsync(set, type, names[file], stream, cancellationToken);
		}
	}
}