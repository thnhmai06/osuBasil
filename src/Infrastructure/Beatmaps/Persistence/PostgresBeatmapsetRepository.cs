using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common.Common;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Common.Registry;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Beatmaps.Persistence;

/// <summary>Stores beatmapsets in the <c>beatmapsets</c> table.</summary>
internal sealed class PostgresBeatmapsetRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	TimeProvider time,
	Lazy<PostgresBeatmapRepository> beatmapRepository)
	: RemovableMemoryRepository<int, BeatmapsetRecord>(reader, writer), IBeatmapsetRepository
{
	/// <summary>
	///     Gets the root a commit is stored under: it changes the rows of several beatmapsets, so it has no single root
	///     of its own and runs after every other commit.
	/// </summary>
	private static Root SyncRoot => Root.Beatmapset(0);

	/// <summary>Gets the repository that stores the difficulties the stored beatmapsets have.</summary>
	private PostgresBeatmapRepository Beatmaps => beatmapRepository.Value;

	/// <inheritdoc />
	public ValueTask<BeatmapsetRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		return FindAsync(id, cancellationToken);
	}

	/// <inheritdoc />
	public Task UpdateAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default)
	{
		_ = SaveAsync(set);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<BeatmapsetRecord>> CommitAsync(BeatmapsetChanges changes,
		CancellationToken cancellationToken = default)
	{
		try
		{
			// The commit replaces or removes stored sets, so what is still queued for them is stored first and
			// nothing queued later is stored before the commit.
			var touched = changes.Store.Where(change => change.Existing is not null).Select(change => change.Existing!)
				.Concat(changes.Remove).Select(set => Root.Beatmapset(set.Id));
			await using var hold = await Writer.HoldAsync(touched, cancellationToken);
			var stored = await Writer.WriteAsync(SyncRoot, async (BeatmapsDbContext context, CancellationToken token) =>
				await StoreAsync(changes, context, token), cancellationToken);
			return Remember(stored, changes.Remove);
		}
		catch (Exception exception) when (exception.IsUniqueViolation())
		{
			throw new AlreadyExistsException("A stored beatmapset or beatmap already has this id or hash.");
		}
	}

	/// <inheritdoc />
	public Task DeleteAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default)
	{
		_ = RemoveAsync(set);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<Page<BeatmapsetRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (total, found) = await ReadUntilStableAsync<(int Total, List<BeatmapsetRecord> Sets)>(async () =>
		{
			var (count, rows) = await Reader.ReadAsync(async (BeatmapsDbContext context) =>
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

			// A set whose deletion is still queued never comes back into memory.
			var loaded = rows.Select(row => row.ToRecord()).Where(set => !IsBeingRemoved(set)).ToList();
			return (count, loaded);
		});

		return new Page<BeatmapsetRecord>(found.Select(Track).ToList(), total);
	}

	protected override int KeyOf(BeatmapsetRecord item)
	{
		return item.Id;
	}

	protected override Root RootOf(BeatmapsetRecord item)
	{
		return Root.Beatmapset(item.Id);
	}

	protected override void CopyTo(BeatmapsetRecord live, BeatmapsetRecord from)
	{
		live.Fingerprint = from.Fingerprint;
		live.Metadata = from.Metadata;
		live.Assets = from.Assets;
		live.UpdatedAt = from.UpdatedAt;
		live.Locked = from.Locked;
		live.Visible = from.Visible;
		live.Status = from.Status;
	}

	protected override async Task<BeatmapsetRecord?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync((BeatmapsDbContext context) => context.Beatmapsets
			.Where(s => s.Id == key)
			.FirstOrDefaultAsync(CancellationToken.None));
		return row?.ToRecord();
	}

	protected override WriteCommand<BeatmapsDbContext> SaveCommand(BeatmapsetRecord set)
	{
		var row = ToRow(set);
		return new WriteCommand<BeatmapsDbContext>((context, token) =>
			context.ReplaceAsync(row, stored => stored.Id == row.Id, token));
	}

	protected override WriteCommand<BeatmapsDbContext> EraseCommand(int key)
	{
		return new WriteCommand<BeatmapsDbContext>((context, token) => context
			.Beatmapsets
			.Where(s => s.Id == key)
			.ExecuteDeleteAsync(token));
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

	/// <summary>Brings the stored beatmapsets in step in memory, once every change is stored.</summary>
	/// <param name="stored">What was stored, in the order of <see cref="BeatmapsetChanges.Store" />.</param>
	/// <param name="removed">The beatmapsets that were removed.</param>
	/// <returns>The live beatmapsets, in the order of <see cref="BeatmapsetChanges.Store" />.</returns>
	private List<BeatmapsetRecord> Remember(List<StoredSet> stored, IReadOnlyCollection<BeatmapsetRecord> removed)
	{
		var beatmaps = Beatmaps;
		var live = new List<BeatmapsetRecord>(stored.Count);

		foreach (var item in stored)
		{
			var set = Live(item.Set);
			var difficulties = item.Beatmaps.Select(storedBeatmap => beatmaps.Apply(set, storedBeatmap)).ToList();
			beatmaps.Replace(set, difficulties);
			live.Add(set);
		}

		foreach (var set in removed)
		{
			beatmaps.Forget(set.Id, set);
			Items.Remove(set.Id);
		}

		return live;
	}

	/// <summary>Gives the live instance of a stored beatmapset its values, or keeps the new one.</summary>
	/// <param name="set">The beatmapset as it is stored.</param>
	/// <returns>The live beatmapset.</returns>
	private BeatmapsetRecord Live(BeatmapsetRecord set)
	{
		var live = Track(set);
		if (!ReferenceEquals(live, set)) CopyTo(live, set);
		return live;
	}
}

/// <summary>One beatmapset, as a commit stored it.</summary>
/// <param name="Set">The beatmapset, with the values it now has.</param>
/// <param name="Beatmaps">The difficulties the set now has, in order.</param>
internal sealed record StoredSet(BeatmapsetRecord Set, List<BeatmapRecord> Beatmaps);