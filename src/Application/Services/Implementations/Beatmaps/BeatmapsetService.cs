using System.Threading.Channels;
using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Application.Services.Implementations.Common;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
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
	public async Task<BeatmapsetImportResult> ImportAsync(Stream archive, CancellationToken cancellationToken = default)
	{
		var content = await reader.ReadAsync(archive, cancellationToken).ConfigureAwait(false);
		return await ImportAsync(content, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<BeatmapsetImportResult> ImportAsync(DirectoryInfo folder,
		CancellationToken cancellationToken = default)
	{
		var content = await reader.ReadAsync(folder, cancellationToken).ConfigureAwait(false);
		return await ImportAsync(content, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Imports what a beatmapset contains, then releases it.</summary>
	/// <param name="content">What was read, or <see langword="null" /> when nothing readable was found.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private async Task<BeatmapsetImportResult> ImportAsync(BeatmapsetContent? content,
		CancellationToken cancellationToken)
	{
		if (content is null) return new BeatmapsetImportResult(null, [], BeatmapsetImportFailure.Unreadable);

		await using (content.ConfigureAwait(false))
		{
			return await ImportContentAsync(content, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>Imports what a beatmapset contains.</summary>
	private async Task<BeatmapsetImportResult> ImportContentAsync(BeatmapsetContent content,
		CancellationToken cancellationToken)
	{
		var hashes = new Dictionary<BeatmapsetDifficulty, Md5>();
		foreach (var difficulty in content.Difficulties)
		{
			var stream = await difficulty.File.OpenAsync(cancellationToken).ConfigureAwait(false);
			await using (stream.ConfigureAwait(false))
			{
				hashes[difficulty] = await Md5.ComputeAsync(stream, cancellationToken).ConfigureAwait(false);
			}
		}

		var stored = new Dictionary<BeatmapsetDifficulty, Beatmap?>();
		foreach (var difficulty in content.Difficulties)
			stored[difficulty] = await beatmaps.GetAsync(hashes[difficulty], cancellationToken).ConfigureAwait(false);

		var existing = stored.Values.FirstOrDefault(b => b is not null)?.Value.Beatmapset
		               ?? (content.OnlineSetId is > 0
			               ? await beatmapsets.GetAsync(content.OnlineSetId.Value, cancellationToken)
				               .ConfigureAwait(false)
			               : null);

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
				}, content.OnlineSetId is > 0 ? content.OnlineSetId : null, cancellationToken).ConfigureAwait(false);
			}
			catch (AlreadyExistsException)
			{
				// Another import created the set meanwhile; fetch it and continue as the existing-set branch.
				set = await beatmapsets.GetAsync(content.OnlineSetId!.Value, cancellationToken).ConfigureAwait(false)
				      ?? throw new InvalidOperationException(
					      "A beatmapset with this online id already exists but could not be retrieved.");
				await UpdateSetAsync(set, content, now, cancellationToken).ConfigureAwait(false);
			}
		}
		else
		{
			await UpdateSetAsync(existing, content, now, cancellationToken).ConfigureAwait(false);
			set = existing;
		}

		await archives.SaveAsync(set, content.Files, cancellationToken).ConfigureAwait(false);

		var result = new List<Beatmap>(content.Difficulties.Count);
		foreach (var difficulty in content.Difficulties)
		{
			var analysis = await AnalyseAsync(difficulty, stored[difficulty], cancellationToken).ConfigureAwait(false);
			var previous = stored[difficulty]
			               ?? (difficulty.OnlineId is > 0
				               ? await beatmaps.GetAsync(difficulty.OnlineId.Value, cancellationToken)
					               .ConfigureAwait(false)
				               : null);

			var data = new BeatmapData
			{
				Hash = hashes[difficulty],
				Beatmapset = set,
				Version = difficulty.Version,
				Difficulty = analysis.Difficulty,
				Objects = analysis.Objects,
				Locked = previous?.Value.Locked ?? false,
				Visible = previous?.Value.Visible ?? true
			};
			Beatmap beatmap;
			if (previous is null)
				try
				{
					beatmap = await beatmaps.CreateAsync(data,
							difficulty.OnlineId is > 0 ? difficulty.OnlineId : null, cancellationToken)
						.ConfigureAwait(false);
				}
				catch (AlreadyExistsException)
				{
					var existingBeatmap = await beatmaps.GetAsync(data.Hash, cancellationToken).ConfigureAwait(false)
					                      ?? (difficulty.OnlineId is > 0
						                      ? await beatmaps.GetAsync(difficulty.OnlineId.Value, cancellationToken)
							                      .ConfigureAwait(false)
						                      : null);
					if (existingBeatmap is null)
						throw;

					beatmap = await UpdateBeatmapAsync(existingBeatmap, data).ConfigureAwait(false);
				}
			else
				beatmap = await UpdateBeatmapAsync(previous, data).ConfigureAwait(false);

			result.Add(beatmap);
		}

		await beatmaps.RetainAsync(set, result, cancellationToken).ConfigureAwait(false);
		_events.Writer.TryWrite(new BeatmapsetImported(set, result));
		return new BeatmapsetImportResult(set, result, null);

		async Task<Beatmap> UpdateBeatmapAsync(Beatmap existingBeatmap, BeatmapData data)
		{
			await beatmaps.UpdateAsync(new Beatmap { Id = existingBeatmap.Id, Value = data },
				cancellationToken).ConfigureAwait(false);
			return existingBeatmap;
		}
	}

	/// <inheritdoc />
	public async Task<int> ScanAsync(CancellationToken cancellationToken = default)
	{
		var allSets = await Paging.ListAllAsync(
			page => beatmapsets.ListAsync(new BeatmapQuery(), page, cancellationToken),
			cancellationToken).ConfigureAwait(false);

		var forgotten = 0;
		foreach (var set in allSets)
			if (!(await archives.ListAsync(set, cancellationToken).ConfigureAwait(false)).Any())
			{
				await beatmaps.RetainAsync(set, [], cancellationToken).ConfigureAwait(false);
				await beatmapsets.DeleteAsync(set, cancellationToken).ConfigureAwait(false);
				_events.Writer.TryWrite(new BeatmapsetDeleted(set));
				forgotten++;
			}

		return forgotten;
	}

	/// <inheritdoc />
	public async Task<bool> DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		if (set.Value.Locked)
			return false;

		await archives.DeleteAsync(set, cancellationToken).ConfigureAwait(false);
		await beatmaps.RetainAsync(set, [], cancellationToken).ConfigureAwait(false);
		await beatmapsets.DeleteAsync(set, cancellationToken).ConfigureAwait(false);
		_events.Writer.TryWrite(new BeatmapsetDeleted(set));
		return true;
	}

	/// <summary>
	///     Analyses a difficulty, reusing the analysis of the stored beatmap with the same file when it has a star
	///     rating.
	/// </summary>
	/// <remarks>A difficulty that cannot be analysed gets a zero star rating and no objects.</remarks>
	private async Task<BeatmapAnalysis> AnalyseAsync(BeatmapsetDifficulty difficulty, Beatmap? stored,
		CancellationToken cancellationToken)
	{
		if (stored is { Value.Difficulty.Star: > 0 })
			return new BeatmapAnalysis(stored.Value.Difficulty, stored.Value.Objects);

		try
		{
			var stream = await difficulty.File.OpenAsync(cancellationToken).ConfigureAwait(false);
			await using (stream.ConfigureAwait(false))
			{
				return analyser.Analyze(stream, difficulty.Mode, GameMods.NoMod);
			}
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			return EmptyAnalysis(difficulty.Mode);
		}
	}

	/// <summary>A zero difficulty and no objects in a game mode.</summary>
	private static BeatmapAnalysis EmptyAnalysis(GameMode mode)
	{
		var objects = BeatmapObjects.Empty(mode);
		return new BeatmapAnalysis(new Difficulty(mode, 0, TimeSpan.Zero, 0, 0, 0, 0, 0), objects);
	}

	private async Task UpdateSetAsync(Beatmapset set, BeatmapsetContent content, DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		set.Value.Artist = content.Artist;
		set.Value.Title = content.Title;
		set.Value.Creator = content.Creator;
		set.Value.UpdatedAt = now;
		await beatmapsets.UpdateAsync(set, cancellationToken).ConfigureAwait(false);
	}
}