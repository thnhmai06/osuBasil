using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Domain.Beatmaps;
using Basil.Domain.Content;


namespace Basil.Infrastructure.Beatmaps.Logic;

internal sealed class BeatmapsetPlanner(
	IBeatmapsetReader reader,
	IBeatmapRepository beatmaps,
	DifficultyAnalyser analyser,
	BeatmapsetMatcher matcher)
{
	internal async Task<(BeatmapsetContent? Content, Stream? Stream)> ReadAsync(BeatmapsetArchive archive,
		CancellationToken ct)
	{
		var stream = await archive.OpenAsync(ct);
		var keep = false;
		try
		{
			var content = await reader.ReadAsync(stream, ct);
			keep = content is not null;
			return (content, stream);
		}
		finally
		{
			if (!keep) await stream.DisposeAsync();
		}
	}

	internal async Task<BeatmapsetPlan> PlanAsync(BeatmapsetArchive archive, ContentFingerprint? fingerprint,
		BeatmapsetContent content,
		int? folderId, CancellationToken ct)
	{
		var setFingerprint = fingerprint ?? await archive.FingerprintAsync(ct);

		var hashes = new Dictionary<BeatmapsetDifficulty, Md5>(content.Difficulties.Count);
		var stored = new Dictionary<BeatmapsetDifficulty, BeatmapRecord?>(content.Difficulties.Count);
		foreach (var difficulty in content.Difficulties)
		{
			var hash = await difficulty.File.HashAsync(ct);
			hashes[difficulty] = hash;
			stored[difficulty] = await beatmaps.GetAsync(hash, ct);
		}

		var existing = await matcher.ResolveAsync(content, hashes, folderId, ct);

		var backgrounds = StoredFileNames.ChooseNames(content.Difficulties.Select(difficulty => difficulty.Background));
		var audio = StoredFileNames.ChooseNames(content.Difficulties.Select(difficulty => difficulty.Audio));
		var video = StoredFileNames.ChooseNames(content.Difficulties.Select(difficulty => difficulty.Video));

		var synced = new List<BeatmapData>(content.Difficulties.Count);

		foreach (var difficulty in content.Difficulties)
			synced.Add(await SyncDifficultyAsync(difficulty, hashes[difficulty], stored[difficulty], backgrounds, audio,
				video, ct));

		var data = new BeatmapsetData(
			existing is null && content.OnlineSetId is > 0 ? content.OnlineSetId : null,
			setFingerprint,
			new BeatmapsetMetadata { Artist = content.Artist, Title = content.Title, Creator = content.Creator },
			new BeatmapsetAssets { HasStoryboard = content.Storyboard is not null },
			synced);

		return new BeatmapsetPlan(archive, existing, data,
			await matcher.TouchesLockedAsync(existing, content, hashes, stored, ct));
	}

	private async Task<BeatmapData> SyncDifficultyAsync(BeatmapsetDifficulty difficulty, Md5 hash,
		BeatmapRecord? stored, IReadOnlyDictionary<BeatmapsetContentFile, string> backgrounds,
		IReadOnlyDictionary<BeatmapsetContentFile, string> audio,
		IReadOnlyDictionary<BeatmapsetContentFile, string> video,
		CancellationToken ct)
	{
		var analysis = await analyser.AnalyseAsync(difficulty, stored, ct);
		var background = difficulty.Background is null ? null : backgrounds[difficulty.Background];
		var sound = difficulty.Audio is null ? null : audio[difficulty.Audio];
		var clip = difficulty.Video is null ? null : video[difficulty.Video];

		return new BeatmapData(
			difficulty.OnlineId is > 0 ? difficulty.OnlineId : null,
			hash,
			new BeatmapMetadata
			{
				Version = difficulty.Version, Difficulty = analysis.Difficulty, ObjectCount = analysis.ObjectCount
			},
			new BeatmapAssets
			{
				HasStoryboard = difficulty.Storyboard,
				BackgroundFile = background,
				AudioFile = sound,
				VideoFile = clip,
				PreviewTime = difficulty.PreviewTime
			});
	}

	internal async Task<BeatmapsetChange> ChangeOfAsync(BeatmapsetRecord set, ContentFingerprint fingerprint,
		CancellationToken ct)
	{
		var synced = (
			from beatmap in await beatmaps.ListAsync(set, ct)
			select new BeatmapData(beatmap.IsLocallyIngested ? null : beatmap.Id, beatmap.Hash, beatmap.Metadata,
				beatmap.Assets)).ToList();

		return new BeatmapsetChange(set, new BeatmapsetData(null, fingerprint, set.Metadata, set.Assets, synced));
	}
}