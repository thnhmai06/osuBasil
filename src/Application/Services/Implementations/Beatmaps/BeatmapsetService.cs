using System.Threading.Channels;
using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Application.Services.Implementations.Common;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Content;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Utilities;

namespace Basil.Application.Services.Implementations.Beatmaps;

/// <summary>
///     Imports, deletes and keeps track of the beatmapsets the server has.
/// </summary>
internal sealed class BeatmapsetService(
	IBeatmapsetReader reader,
	IBeatmapAnalyser analyser,
	IBeatmapsetRepository beatmapsets,
	IBeatmapRepository beatmaps,
	IBeatmapsetStorage archives,
	TimeProvider time) : IBeatmapsetService
{
	private readonly Channel<BeatmapsetEvent> _events = Channel.CreateUnbounded<BeatmapsetEvent>();

	/// <inheritdoc />
	public ChannelReader<BeatmapsetEvent> Events => _events.Reader;

	/// <inheritdoc />
	public async Task<BeatmapsetImportResult> ImportAsync(Stream archive, int? beatmapsetId = null,
		CancellationToken cancellationToken = default)
	{
		await using var copy = new MemoryStream();
		await archive.CopyToAsync(copy, cancellationToken);
		copy.Position = 0;

		if (await reader.ReadAsync(copy, cancellationToken) is not { } content)
			return new BeatmapsetImportResult(null, [], BeatmapsetImportFailure.Unreadable);

		var stored = new Dictionary<BeatmapArchiveDifficulty, Beatmap?>();
		foreach (var difficulty in content.Difficulties)
			stored[difficulty] = await beatmaps.GetAsync(new Md5(difficulty.Content), cancellationToken);

		var existing = stored.Values.FirstOrDefault(b => b is not null)?.Value.Beatmapset
			?? (beatmapsetId is { } named ? await beatmapsets.GetAsync(named, cancellationToken) : null)
			?? (content.OnlineSetId is > 0 ? await beatmapsets.GetAsync(content.OnlineSetId.Value, cancellationToken) : null);

		if (existing is { Value.Locked: true })
			return new BeatmapsetImportResult(null, [], BeatmapsetImportFailure.Locked);

		var now = time.GetUtcNow();
		Beatmapset set;
		if (existing is null)
		{
			try
			{
				set = await beatmapsets.CreateAsync(new BeatmapsetData
				{
					Artist = content.Artist,
					Title = content.Title,
					Creator = content.Creator,
					CreatedAt = now,
					UpdatedAt = now
				}, content.OnlineSetId is > 0 ? content.OnlineSetId : null, cancellationToken);
			}
			catch (AlreadyExistsException)
			{
				// Another import created the set meanwhile; fetch it and continue as the existing-set branch.
				set = await beatmapsets.GetAsync(content.OnlineSetId!.Value, cancellationToken)
					?? throw new InvalidOperationException("A beatmapset with this online id already exists but could not be retrieved.");
				await UpdateSetAsync(set, content, now, cancellationToken);
			}
		}
		else
		{
			await UpdateSetAsync(existing, content, now, cancellationToken);
			set = existing;
		}

		copy.Position = 0;
		await archives.SaveAsync(set, copy, cancellationToken);

		var result = new List<Beatmap>(content.Difficulties.Count);
		foreach (var difficulty in content.Difficulties)
		{
			var analysis = Analyse(difficulty, stored[difficulty]);
			var previous = stored[difficulty]
				?? (difficulty.OnlineId is > 0 ? await beatmaps.GetAsync(difficulty.OnlineId.Value, cancellationToken) : null);

			var data = new BeatmapData
			{
				Hash = new Md5(difficulty.Content),
				Beatmapset = set,
				Version = difficulty.Version,
				Difficulty = analysis.Difficulty,
				Objects = analysis.Objects,
				Locked = previous?.Value.Locked ?? false,
				Visible = previous?.Value.Visible ?? true
			};
			Beatmap beatmap;
			if (previous is null)
			{
				try
				{
					beatmap = await beatmaps.CreateAsync(data,
						difficulty.OnlineId is > 0 ? difficulty.OnlineId : null, cancellationToken);
				}
				catch (AlreadyExistsException)
				{
					var existingBeatmap = await beatmaps.GetAsync(data.Hash, cancellationToken)
						?? (difficulty.OnlineId is > 0
							? await beatmaps.GetAsync(difficulty.OnlineId.Value, cancellationToken)
							: null);
					if (existingBeatmap is null)
						throw;

					beatmap = await UpdateBeatmapAsync(existingBeatmap, data);
				}
			}
			else
			{
				beatmap = await UpdateBeatmapAsync(previous, data);
			}

			result.Add(beatmap);
		}

		await beatmaps.RetainAsync(set, result, cancellationToken);
		_events.Writer.TryWrite(new BeatmapsetImported(set, result));
		return new BeatmapsetImportResult(set, result, null);

		async Task<Beatmap> UpdateBeatmapAsync(Beatmap existingBeatmap, BeatmapData data)
		{
			await beatmaps.CreateOrUpdateAsync(new Beatmap { Id = existingBeatmap.Id, Value = data }, cancellationToken);
			return existingBeatmap;
		}
	}

	/// <inheritdoc />
	public async Task<int> ScanAsync(CancellationToken cancellationToken = default)
	{
		var allSets = await Paging.ListAllAsync(
			page => beatmapsets.ListAsync(new BeatmapQuery(IncludeHidden: true), page, cancellationToken),
			cancellationToken);

		var forgotten = 0;
		foreach (var set in allSets)
		{
			if ((await archives.ListAsync(set, cancellationToken)).Count == 0)
			{
				await beatmaps.RetainAsync(set, [], cancellationToken);
				await beatmapsets.DeleteAsync(set, cancellationToken);
				_events.Writer.TryWrite(new BeatmapsetDeleted(set));
				forgotten++;
			}
		}

		return forgotten;
	}

	/// <inheritdoc />
	public async Task<bool> DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		if (set.Value.Locked)
			return false;

		await archives.DeleteAsync(set, cancellationToken);
		await beatmaps.RetainAsync(set, [], cancellationToken);
		await beatmapsets.DeleteAsync(set, cancellationToken);
		_events.Writer.TryWrite(new BeatmapsetDeleted(set));
		return true;
	}

	/// <summary>
	///     Analyses a difficulty, reusing the analysis of the stored beatmap with the same file when it has a star
	///     rating.
	/// </summary>
	/// <remarks>A difficulty that cannot be analysed gets a zero star rating and no objects.</remarks>
	private BeatmapAnalysis Analyse(BeatmapArchiveDifficulty difficulty, Beatmap? stored)
	{
		if (stored is { Value.Difficulty.Star: > 0 })
			return new BeatmapAnalysis(stored.Value.Difficulty, stored.Value.Objects);

		try
		{
			return analyser.Analyze(difficulty.Content, difficulty.Mode, GameMods.NoMod);
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			return EmptyAnalysis(difficulty.Mode);
		}
	}

	/// <summary>A zero difficulty and no objects in a game mode.</summary>
	private static BeatmapAnalysis EmptyAnalysis(GameMode mode)
	{
		var objects = BeatmapObjects.NewFrom(mode);
		return new BeatmapAnalysis(new Difficulty(mode, 0, TimeSpan.Zero, 0, 0, 0, 0, 0), objects);
	}

	private async Task UpdateSetAsync(Beatmapset set, BeatmapsetArchive content, DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		set.Value.Artist = content.Artist;
		set.Value.Title = content.Title;
		set.Value.Creator = content.Creator;
		set.Value.UpdatedAt = now;
		await beatmapsets.CreateOrUpdateAsync(set, cancellationToken);
	}
}
