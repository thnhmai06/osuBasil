using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Basil.Domain.Game.Beatmaps;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Beatmaps.Persistence;

internal sealed class EfBeatmapsetRepository(DatabaseSession database, TimeProvider time) : IBeatmapsetRepository
{
	public async ValueTask<BeatmapsetRecord?> GetAsync(int id, CancellationToken ct = default)
	{
		var row = await database.ReadAsync((BeatmapsDbContext context) => context.Beatmapsets
			.Where(s => s.Id == id)
			.FirstOrDefaultAsync(ct), ct);
		return row?.ToRecord();
	}

	public Task UpdateAsync(BeatmapsetRecord set)
	{
		var row = ToRow(set);
		return database.WriteAsync((BeatmapsDbContext context) =>
			context.UpsertAsync(row, stored => stored.Id == row.Id));
	}

	public async Task<IReadOnlyList<BeatmapsetRecord>> CommitAsync(BeatmapsetChanges changes)
	{
		List<StoredSet> stored = [];
		await database.TransactAsync(async (BeatmapsDbContext context) =>
			stored = await StoreAsync(changes, context));
		return [.. stored.Select(item => item.Set)];
	}

	public Task DeleteAsync(BeatmapsetRecord set)
	{
		return database.WriteAsync((BeatmapsDbContext context) => context.Beatmapsets
			.Where(s => s.Id == set.Id)
			.ExecuteDeleteAsync());
	}

	public async Task<Page<BeatmapsetRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken ct = default)
	{
		var fetched = await database.ReadAsync((BeatmapsDbContext context) =>
		{
			// A set matches when one of its difficulties does.
			var matching = BeatmapQueryFilter.Apply(context.Beatmaps, query);
			return context.Beatmapsets.Where(set => matching.Any(beatmap => beatmap.BeatmapsetId == set.Id))
				.OrderByDescending(set => set.Id)
				.Skip(page.Offset)
				.Take(page.Fetch)
				.ToListAsync(ct);
		}, ct);

		var (rows, hasNext) = page.Slice(fetched);
		return new Page<BeatmapsetRecord>([.. rows.Select(row => row.ToRecord())], hasNext);
	}

	public async Task<IReadOnlyList<int>> ListIdsAsync(CancellationToken ct = default)
	{
		return await database.ReadAsync(async (BeatmapsDbContext context) => await context.Beatmapsets
			.OrderByDescending(set => set.Id)
			.Select(set => set.Id)
			.Take(ListLimits.Ceiling)
			.ToListAsync(ct), ct);
	}

	private async Task<List<StoredSet>> StoreAsync(BeatmapsetChanges changes, BeatmapsDbContext context)
	{
		var now = time.GetUtcNow();
		var stored = new List<StoredSet>(changes.Store.Count);
		// The difficulties of a replaced or removed set may move to another set of the same commit.
		var releasable = changes.Store.Where(change => change.Existing is not null)
			.Select(change => change.Existing!.Id)
			.Concat(changes.Remove.Select(removed => removed.Id)).ToHashSet();

		foreach (var (existing, data) in changes.Store)
			stored.Add(await StoreOneAsync(existing, data, now, releasable, context));

		foreach (var set in changes.Remove)
		{
			await context.Beatmaps
				.Where(b => b.BeatmapsetId == set.Id)
				.ExecuteDeleteAsync();
			await context.Beatmapsets
				.Where(s => s.Id == set.Id)
				.ExecuteDeleteAsync();
		}

		return stored;
	}

	private static async Task<StoredSet> StoreOneAsync(BeatmapsetRecord? existing, BeatmapsetData data,
		DateTimeOffset now,
		IReadOnlySet<int> releasable, BeatmapsDbContext context)
	{
		var knownId = existing?.Id ?? data.OnlineId;
		// A record needs an id; the row of a set without an osu! id has none until a local id is taken.
		var row = ToRow(Build(existing, data, now, knownId ?? 1));
		if (knownId is null)
			row.Id = 0;

		if (existing is null)
		{
			// A new set never replaces a stored one: an id another set has is refused by the database.
			if (row.Id == 0)
				row.Id = (int)await IdCounters.NextAsync(context, BeatmapsDbContext.LocalBeatmapsetIds);
			context.Beatmapsets.Add(row);
			await context.SaveChangesAsync();
		}
		else
		{
			await context.UpsertAsync(row, candidate => candidate.Id == row.Id);
		}

		var set = Build(existing, data, now, row.Id);
		var beatmaps =
			await EfBeatmapRepository.StoreAsync(set, data.Beatmaps, now, releasable, context);

		var keepIds = beatmaps.Select(beatmap => beatmap.Id).ToArray();
		await context.Beatmaps
			.Where(b => b.BeatmapsetId == set.Id && !((IEnumerable<int>)keepIds).Contains(b.Id))
			.ExecuteDeleteAsync();

		return new StoredSet(set, beatmaps);
	}

	private static BeatmapsetRecord Build(BeatmapsetRecord? existing, BeatmapsetData data, DateTimeOffset now, int id)
	{
		return new BeatmapsetRecord
		{
			Id = id,
			Content = data.Fingerprint,
			Metadata = data.Metadata,
			Assets = data.Assets,
			CreatedAt = existing?.CreatedAt ?? now,
			UpdatedAt = now,
			Locked = existing?.Locked ?? false,
			Visible = existing?.Visible ?? true,
			Status = existing?.Status ?? BeatmapsetStatus.Ranked
		};
	}

	private static BeatmapsetRow ToRow(BeatmapsetRecord set)
	{
		return new BeatmapsetRow
		{
			Id = set.Id,
			Artist = set.Metadata.Artist,
			Title = set.Metadata.Title,
			Creator = set.Metadata.Creator,
			CreatedAt = set.CreatedAt.ToUniversalTime(),
			UpdatedAt = set.UpdatedAt.ToUniversalTime(),
			ArchiveHash = set.Content.Hash.HashValue,
			ArchiveSize = set.Content.Metadata.Size,
			ArchiveModifiedAt = set.Content.Metadata.ModifiedAt.ToUniversalTime(),
			Storyboard = set.Assets.HasStoryboard,
			Locked = set.Locked,
			Visible = set.Visible,
			Status = set.Status
		};
	}
}

internal sealed record StoredSet(BeatmapsetRecord Set, List<BeatmapRecord> Beatmaps);