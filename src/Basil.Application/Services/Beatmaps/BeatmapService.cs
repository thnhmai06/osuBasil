using System.Threading.Channels;
using Basil.Application.Beatmaps;
using Basil.Application.Common;
using Basil.Application.Contracts.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Utilities;

namespace Basil.Application.Services.Beatmaps;

/// <summary>
///     Imports, deletes and keeps track of the beatmapsets the server has.
/// </summary>
internal sealed class BeatmapService(
	IBeatmapArchiveReader reader,
	IBeatmapAnalyser analyser,
	IBeatmapsetRepository beatmapsets,
	IBeatmapRepository beatmaps,
	IBeatmapArchiveStorage archives,
	TimeProvider time) : IBeatmapService
{
	private readonly Channel<BeatmapsetEvent> _events = Channel.CreateUnbounded<BeatmapsetEvent>();

	/// <inheritdoc />
	public ChannelReader<BeatmapsetEvent> Events => _events.Reader;

	/// <inheritdoc />
	public async Task<BeatmapImportResult> ImportAsync(Stream archive, int? beatmapsetId = null,
		CancellationToken cancellationToken = default)
	{
		await using var copy = new MemoryStream();
		await archive.CopyToAsync(copy, cancellationToken);
		copy.Position = 0;

		if (await reader.ReadAsync(copy, cancellationToken) is not { } content)
			return new BeatmapImportResult(null, [], BeatmapImportFailure.Unreadable);

		var stored = new Dictionary<BeatmapArchiveDifficulty, Beatmap?>();
		foreach (var difficulty in content.Difficulties)
			stored[difficulty] = await beatmaps.GetByHashAsync(new Md5(difficulty.Content), cancellationToken);

		var setId = stored.Values.FirstOrDefault(b => b is not null)?.Beatmapset.Id;
		if (setId is null && beatmapsetId is { } named && await beatmapsets.GetAsync(named, cancellationToken) is not null)
			setId = named;
		setId ??= content.OnlineSetId is > 0 ? content.OnlineSetId : await NewLocalIdAsync(cancellationToken);

		var existing = await beatmapsets.GetAsync(setId.Value, cancellationToken);
		if (existing is { Locked: true })
			return new BeatmapImportResult(null, [], BeatmapImportFailure.Locked);

		var now = time.GetUtcNow();
		var set = new Beatmapset
		{
			Id = setId.Value, Artist = content.Artist, Title = content.Title, Creator = content.Creator, UpdatedAt = now,
			CreatedAt = existing?.CreatedAt ?? now, Visible = existing?.Visible ?? true
		};

		copy.Position = 0;
		await archives.SaveAsync(set, copy, cancellationToken);
		await beatmapsets.CreateOrUpdateAsync(set, cancellationToken);

		var result = new List<Beatmap>(content.Difficulties.Count);
		foreach (var difficulty in content.Difficulties)
		{
			var analysis = analyser.Analyze(difficulty.Content, difficulty.Mode, GameMods.NoMod);
			var beatmap = new Beatmap
			{
				Id = stored[difficulty]?.Id ?? (difficulty.OnlineId is > 0 ? difficulty.OnlineId.Value : 0),
				Hash = new Md5(difficulty.Content),
				Beatmapset = set,
				Version = difficulty.Version,
				Difficulty = analysis.Difficulty,
				Objects = analysis.Objects
			};
			await beatmaps.CreateOrUpdateAsync(beatmap, cancellationToken);
			result.Add(beatmap);
		}

		await beatmaps.RetainAsync(set, result, cancellationToken);

		_events.Writer.TryWrite(new BeatmapsetImported(set, result));
		return new BeatmapImportResult(set, result, null);
	}

	/// <summary>Picks an id for a beatmapset that is known nowhere else: above every stored id and at least 1 000 000 000.</summary>
	private async Task<int> NewLocalIdAsync(CancellationToken cancellationToken)
	{
		var newest = await beatmapsets.ListAsync(new BeatmapQuery(IncludeHidden: true), new PageRequest(0, 1),
			cancellationToken);
		return Math.Max(1_000_000_000, (newest.Items.FirstOrDefault()?.Id ?? 0) + 1);
	}


	/// <inheritdoc />
	public async Task<bool> DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		if (set.Locked)
			return false;

		await archives.DeleteAsync(set, cancellationToken);
		await beatmapsets.DeleteAsync(set, cancellationToken);
		_events.Writer.TryWrite(new BeatmapsetDeleted(set));
		return true;
	}

	/// <inheritdoc />
	public async Task<int> ScanAsync(CancellationToken cancellationToken = default)
	{
		var page = new PageRequest(0, 100);
		var allSets = new List<Beatmapset>();

		while (true)
		{
			var paged = await beatmapsets.ListAsync(new BeatmapQuery(IncludeHidden: true), page, cancellationToken);
			allSets.AddRange(paged.Items);
			if (paged.Items.Count < page.Limit)
				break;
			page = new PageRequest(page.Offset + page.Limit, page.Limit);
		}

		var forgotten = 0;
		foreach (var set in allSets)
		{
			var archiveStream = await archives.OpenAsync(set, cancellationToken);
			if (archiveStream is null)
			{
				await beatmapsets.DeleteAsync(set, cancellationToken);
				_events.Writer.TryWrite(new BeatmapsetDeleted(set));
				forgotten++;
			}
			else
			{
				await archiveStream.DisposeAsync();
			}
		}

		return forgotten;
	}
}