using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;

namespace Basil.Infrastructure.Beatmaps.Logic;

/// <summary>Makes the files a stored beatmapset serves from its archive.</summary>
internal sealed class BeatmapsetFileWriter(
	IBeatmapsetReader reader,
	IBeatmapsetStorage storage,
	IBeatmapRepository beatmaps,
	IBeatmapAssets assets)
{
	/// <summary>Makes every file a set serves from its archive, which is already stored.</summary>
	/// <param name="set">The beatmapset, whose archive is stored.</param>
	/// <remarks>The names are chosen the same way they were when the set was planned, so they match the records.</remarks>
	internal async Task StoreFilesAsync(BeatmapsetRecord set)
	{
		await storage.ClearAsync(set);

		var stream = await storage.OpenArchiveAsync(set, true);
		if (stream is null) return;

		var keep = false;
		List<string> videoFiles;
		try
		{
			var content = await reader.ReadAsync(stream);
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
					var hash = await difficulty.File.HashAsync();
					var osu = await difficulty.File.OpenAsync();
					await using (osu)
					{
						await storage.SaveAsync(set, BeatmapsetFileType.Version, $"{hash}.osu", osu);
					}
				}

				var done = new HashSet<BeatmapsetContentFile>();
				foreach (var difficulty in content.Difficulties)
				{
					await SaveFileAsync(set, BeatmapsetFileType.Background, difficulty.Background, backgrounds, done);
					await SaveFileAsync(set, BeatmapsetFileType.Audio, difficulty.Audio, audio, done);
					await SaveFileAsync(set, BeatmapsetFileType.Video, difficulty.Video, video, done);
				}

				if (content.Storyboard is not null)
				{
					var storyboard = await content.Storyboard.OpenAsync();
					await using (storyboard)
					{
						await storage.SaveAsync(set, BeatmapsetFileType.Storyboard, StoredFileNames.Storyboard,
							storyboard);
					}
				}
			}
		}
		finally
		{
			if (!keep) await stream.DisposeAsync();
		}

		await storage.SaveArchiveWithoutVideoAsync(set, videoFiles);
		var storedBeatmaps = await beatmaps.ListAsync(set);
		await assets.BuildAsync(set, storedBeatmaps);
	}

	/// <summary>Stores one file of a set, once, under the name chosen for it.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="type">What the file is.</param>
	/// <param name="file">The file of the set's archive, or <see langword="null" /> when the set has none of that kind.</param>
	/// <param name="names">The name chosen for each file.</param>
	/// <param name="done">The files already stored by this call; <paramref name="file" /> is added to it.</param>
	private async Task SaveFileAsync(BeatmapsetRecord set, BeatmapsetFileType type, BeatmapsetContentFile? file,
		IReadOnlyDictionary<BeatmapsetContentFile, string> names, HashSet<BeatmapsetContentFile> done)
	{
		if (file is null || !done.Add(file)) return;

		var stream = await file.OpenAsync();
		await using (stream)
		{
			await storage.SaveAsync(set, type, names[file], stream);
		}
	}
}