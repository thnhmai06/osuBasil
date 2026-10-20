using Basil.Application.Storage.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Content;

namespace Basil.Application.Services.Beatmaps;

/// <summary>Reads archives and plans the beatmapsets they hold.</summary>
internal sealed class BeatmapsetPlanner(
	IBeatmapsetReader reader,
	IBeatmapRepository beatmaps,
	DifficultyAnalyser analyser,
	BeatmapsetMatcher matcher)
{
	/// <summary>Reads an archive lying in the storage, keeping it open while its content lives.</summary>
	/// <param name="archive">The archive to read.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     What the archive contains and the open stream it is read from; both <see langword="null" /> when it is not a
	///     readable beatmapset archive. The caller releases the content first and the stream after it.
	/// </returns>
	internal async Task<(BeatmapsetContent? Content, Stream? Stream)> ReadAsync(BeatmapsetArchive archive,
		CancellationToken cancellationToken)
	{
		var stream = await archive.OpenAsync(cancellationToken).ConfigureAwait(false);
		var keep = false;
		try
		{
			var content = await reader.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
			keep = content is not null;
			return (content, stream);
		}
		finally
		{
			if (!keep) await stream.DisposeAsync().ConfigureAwait(false);
		}
	}

	/// <summary>Plans one archive's beatmapset: its records and the name of every file it serves.</summary>
	/// <param name="archive">The archive to plan.</param>
	/// <param name="fingerprint">The archive's fingerprint when it is already taken, or <see langword="null" />.</param>
	/// <param name="content">What the archive contains.</param>
	/// <param name="folderId">The id of the stored folder the archive lies in, or <see langword="null" /> when it lies loose.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The archive, the stored beatmapset it belongs to, and the set as read from it.</returns>
	internal async Task<BeatmapsetPlan> PlanAsync(BeatmapsetArchive archive, ContentFingerprint? fingerprint,
		BeatmapsetContent content,
		int? folderId, CancellationToken cancellationToken)
	{
		var setFingerprint = fingerprint ?? await archive.FingerprintAsync(cancellationToken).ConfigureAwait(false);

		var hashes = new Dictionary<BeatmapsetDifficulty, Md5>(content.Difficulties.Count);
		var stored = new Dictionary<BeatmapsetDifficulty, BeatmapRecord?>(content.Difficulties.Count);
		foreach (var difficulty in content.Difficulties)
		{
			var hash = await difficulty.File.HashAsync(cancellationToken).ConfigureAwait(false);
			hashes[difficulty] = hash;
			stored[difficulty] = await beatmaps.GetAsync(hash, cancellationToken).ConfigureAwait(false);
		}

		var existing = await matcher.ResolveAsync(content, hashes, folderId, cancellationToken).ConfigureAwait(false);

		var backgrounds = StoredFileNames.ChooseNames(content.Difficulties.Select(difficulty => difficulty.Background));
		var audio = StoredFileNames.ChooseNames(content.Difficulties.Select(difficulty => difficulty.Audio));
		var video = StoredFileNames.ChooseNames(content.Difficulties.Select(difficulty => difficulty.Video));

		var synced = new List<BeatmapData>(content.Difficulties.Count);

		foreach (var difficulty in content.Difficulties)
		{
			var analysis = await analyser.AnalyseAsync(difficulty, stored[difficulty], cancellationToken)
				.ConfigureAwait(false);
			var background = difficulty.Background is null ? null : backgrounds[difficulty.Background];
			var sound = difficulty.Audio is null ? null : audio[difficulty.Audio];
			var clip = difficulty.Video is null ? null : video[difficulty.Video];

			synced.Add(new BeatmapData(
				difficulty.OnlineId is > 0 ? difficulty.OnlineId : null,
				hashes[difficulty],
				new BeatmapMetadata
				{
					Version = difficulty.Version, Difficulty = analysis.Difficulty, Objects = analysis.Objects
				},
				new BeatmapAssets
				{
					HasStoryboard = difficulty.Storyboard,
					BackgroundFile = background,
					AudioFile = sound,
					VideoFile = clip,
					PreviewTime = difficulty.PreviewTime
				}));
		}

		var data = new BeatmapsetData(
			existing is null && content.OnlineSetId is > 0 ? content.OnlineSetId : null,
			setFingerprint,
			new BeatmapsetMetadata { Artist = content.Artist, Title = content.Title, Creator = content.Creator },
			new BeatmapsetAssets { HasStoryboard = content.Storyboard is not null },
			synced);

		return new BeatmapsetPlan(archive, existing, data,
			await matcher.TouchesLockedAsync(existing, content, hashes, stored, cancellationToken)
				.ConfigureAwait(false));
	}

	/// <summary>Describes a stored beatmapset as it is, with its archive's new fingerprint.</summary>
	/// <param name="set">The stored beatmapset, with its difficulties as they are stored.</param>
	/// <param name="fingerprint">The fingerprint of the set's archive, unchanged in content.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	internal async Task<BeatmapsetChange> ChangeOfAsync(BeatmapsetRecord set, ContentFingerprint fingerprint,
		CancellationToken cancellationToken)
	{
		var synced = (
			from beatmap in await beatmaps.ListAsync(set, cancellationToken).ConfigureAwait(false)
			select new BeatmapData(beatmap.IsLocallyIngested ? null : beatmap.Id, beatmap.Hash, beatmap.Metadata,
				beatmap.Assets)).ToList();

		return new BeatmapsetChange(set, new BeatmapsetData(null, fingerprint, set.Metadata, set.Assets, synced));
	}
}
