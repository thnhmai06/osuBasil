using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Beatmaps.Persistence;

/// <summary>Stores beatmapsets in the <c>beatmapsets</c> table.</summary>
internal sealed class PostgresBeatmapsetRepository(DatabaseSession database, TimeProvider time) : IBeatmapsetRepository
{
	/// <inheritdoc />
	public async ValueTask<BeatmapsetRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		var row = await database.ReadAsync((BeatmapsDbContext context) => context.Beatmapsets
			.Where(s => s.Id == id)
			.FirstOrDefaultAsync(cancellationToken), cancellationToken);
		return row?.ToRecord();
	}

	/// <inheritdoc />
	public Task UpdateAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default)
	{
		var row = ToRow(set);
		return database.WriteAsync((BeatmapsDbContext context, CancellationToken token) =>
			context.UpsertAsync(row, stored => stored.Id == row.Id, token), cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<BeatmapsetRecord>> CommitAsync(BeatmapsetChanges changes,
		CancellationToken cancellationToken = default)
	{
		List<StoredSet> stored = [];
		await database.TransactAsync(async (BeatmapsDbContext context, CancellationToken token) =>
			stored = await StoreAsync(changes, context, token), cancellationToken);
		return [.. stored.Select(item => item.Set)];
	}

	/// <inheritdoc />
	public Task DeleteAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default)
	{
		return database.WriteAsync((BeatmapsDbContext context, CancellationToken token) => context.Beatmapsets
			.Where(s => s.Id == set.Id)
			.ExecuteDeleteAsync(token), cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Page<BeatmapsetRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (total, rows) = await database.ReadAsync(async (BeatmapsDbContext context) =>
		{
			// A set matches when one of its difficulties does.
			var matching = BeatmapQueryFilter.Apply(context.Beatmaps, query);
			var sets = context.Beatmapsets.Where(set => matching.Any(beatmap => beatmap.BeatmapsetId == set.Id));
			var count = await sets.CountAsync(cancellationToken);
			var rows = await sets
				.OrderByDescending(set => set.Id)
				.Skip(page.Offset)
				.Take(page.Limit)
				.ToListAsync(cancellationToken);
			return (count, rows);
		}, cancellationToken);

		return new Page<BeatmapsetRecord>([.. rows.Select(row => row.ToRecord())], total);
	}

	/// <summary>Stores every change in one transaction, keeping what a replaced beatmapset was stored with.</summary>
	/// <param name="changes">The beatmapsets to store and to remove.</param>
	/// <param name="context">The EF context the transaction runs on.</param>
	/// <param name="token">The cancellation token.</param>
	/// <returns>What was stored, in the order of <see cref="BeatmapsetChanges.Store" />.</returns>
	private async Task<List<StoredSet>> StoreAsync(BeatmapsetChanges changes, BeatmapsDbContext context,
		CancellationToken token)
	{
		var now = time.GetUtcNow();
		var stored = new List<StoredSet>(changes.Store.Count);
		// The difficulties of a replaced or removed set may move to another set of the same commit.
		var releasable = changes.Store.Where(change => change.Existing is not null)
			.Select(change => change.Existing!.Id)
			.Concat(changes.Remove.Select(removed => removed.Id)).ToHashSet();

		foreach (var (existing, data) in changes.Store)
			stored.Add(await StoreOneAsync(existing, data, now, releasable, context, token));

		foreach (var set in changes.Remove)
		{
			await context.Beatmaps
				.Where(b => b.BeatmapsetId == set.Id)
				.ExecuteDeleteAsync(token);
			await context.Beatmapsets
				.Where(s => s.Id == set.Id)
				.ExecuteDeleteAsync(token);
		}

		return stored;
	}

	private static async Task<StoredSet> StoreOneAsync(BeatmapsetRecord? existing, BeatmapsetData data,
		DateTimeOffset now,
		IReadOnlySet<int> releasable, BeatmapsDbContext context, CancellationToken token)
	{
		var knownId = existing?.Id ?? data.OnlineId;
		// A record needs an id; the row of a set without an osu! id has none until the sequence gives it one.
		var row = ToRow(Build(existing, data, now, knownId ?? 1));
		if (knownId is null)
			row.Id = 0;

		if (existing is null)
		{
			// A new set never replaces a stored one: an id another set has is refused by the database.
			context.Beatmapsets.Add(row);
			await context.SaveChangesAsync(token);
		}
		else
		{
			await context.UpsertAsync(row, candidate => candidate.Id == row.Id, token);
		}

		var set = Build(existing, data, now, row.Id);
		var beatmaps =
			await PostgresBeatmapRepository.StoreAsync(set, data.Beatmaps, now, releasable, context, token);

		var keepIds = beatmaps.Select(beatmap => beatmap.Id).ToArray();
		await context.Beatmaps
			.Where(b => b.BeatmapsetId == set.Id && !((IEnumerable<int>)keepIds).Contains(b.Id))
			.ExecuteDeleteAsync(token);

		return new StoredSet(set, beatmaps);
	}

	private static BeatmapsetRecord Build(BeatmapsetRecord? existing, BeatmapsetData data, DateTimeOffset now, int id)
	{
		return new BeatmapsetRecord
		{
			Id = id,
			Fingerprint = data.Fingerprint,
			Metadata = data.Metadata,
			Assets = data.Assets,
			CreatedAt = existing?.CreatedAt ?? now,
			UpdatedAt = now,
			Locked = existing?.Locked ?? false,
			Visible = existing?.Visible ?? true,
			Status = existing?.Status ?? BeatmapsetStatus.Ranked
		};
	}

	/// <summary>Makes the row that stores a beatmapset, with the values it has now.</summary>
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
			ArchiveHash = set.Fingerprint.Hash.HashValue,
			ArchiveSize = set.Fingerprint.Stamp.Size,
			ArchiveModifiedAt = set.Fingerprint.Stamp.ModifiedAt.ToUniversalTime(),
			Storyboard = set.Assets.HasStoryboard,
			Locked = set.Locked,
			Visible = set.Visible,
			Status = set.Status
		};
	}
}

/// <summary>One beatmapset, as a commit stored it.</summary>
/// <param name="Set">The beatmapset, with the values it now has.</param>
/// <param name="Beatmaps">The difficulties the set now has, in order.</param>
internal sealed record StoredSet(BeatmapsetRecord Set, List<BeatmapRecord> Beatmaps);