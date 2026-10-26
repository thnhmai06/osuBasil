using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Basil.Application.Persistence.Beatmaps;
using Basil.Application.Persistence.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Infrastructure.Persistence.Memory;
using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Beatmaps;

/// <summary>Stores beatmap difficulties in the <c>beatmaps</c> table.</summary>
internal sealed class PostgresBeatmapRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IBeatmapsetRepository beatmapsets)
	: MemoryRepository<int, BeatmapRecord>(reader, writer), IBeatmapRepository
{
	// jsonb keeps object keys in its own order, so the mode discriminator need not come first.
	// The objects of each mode are stored with a mode discriminator; jsonb keeps object keys in its own order.
	private static readonly JsonSerializerOptions StoredJson = new()
	{
		AllowOutOfOrderMetadataProperties = true,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { StoredObjects } }
	};

	private readonly WeakIndex<Md5, BeatmapRecord> _byHash = new();
	private readonly OwnedLists<int, BeatmapsetRecord, BeatmapRecord> _bySet = new();

	// The beatmaps a set keeps while the deletion of its others is queued, by set id.
	private readonly ConcurrentDictionary<int, HashSet<int>> _retaining = new();

	/// <inheritdoc />
	public async Task UpdateAsync(BeatmapRecord beatmap, CancellationToken cancellationToken = default)
	{
		var value = Copy(beatmap, await LiveSetAsync(beatmap.Beatmapset, cancellationToken));
		if (!Items.TryGetValue(beatmap.Id, out var current) || current.Hash != value.Hash)
		{
			if (_byHash.TryGetValue(value.Hash, out var known) && known.Id != beatmap.Id &&
			    known.Hash == value.Hash)
				throw new AlreadyExistsException($"A beatmap already has the hash {value.Hash}.");

			// A hash must be unique across every beatmap, so a new or changed one is written at once and the live
			// beatmap takes it only after the commit.
			try
			{
				var row = ToRow(value);
				await Writer.WriteAsync(Root.Beatmapset(value.Beatmapset.Id),
					(context, token) => context.UpsertAsync(row, stored => stored.Id == row.Id, token),
					cancellationToken);
			}
			catch (Exception exception) when (exception.IsUniqueViolation())
			{
				throw new AlreadyExistsException($"A beatmap already has the hash {value.Hash}.");
			}

			Remember(beatmap, value, Task.CompletedTask);
			return;
		}

		var saved = SaveAsync(beatmap);
		var live = Track(beatmap);
		Remember(live, value, saved);
	}

	/// <inheritdoc />
	public async ValueTask<BeatmapRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		var beatmap = await FindAsync(id, cancellationToken);
		if (beatmap is not null)
			Index(beatmap);
		return beatmap;
	}

	/// <inheritdoc />
	public async ValueTask<BeatmapRecord?> GetAsync(Md5 hash, CancellationToken cancellationToken = default)
	{
		if (_byHash.TryGetValue(hash, out var known) && known.Hash == hash)
			return known;

		var loaded = await ReadUntilStableAsync(async () =>
		{
			var row = await Reader.ReadAsync(context => context.Beatmaps
				.Include(b => b.Set)
				.Where(b => b.Hash == hash.HashValue)
				.FirstOrDefaultAsync(cancellationToken), cancellationToken);
			if (row is null)
				return null;

			// A row whose deletion is still queued never comes back into memory.
			var stored = await ToBeatmapAsync(row, cancellationToken);
			return IsBeingRemoved(stored) ? null : stored;
		});
		if (loaded is null)
			return null;

		var beatmap = Track(loaded);
		Index(beatmap);
		return beatmap;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<BeatmapRecord>> ListAsync(BeatmapsetRecord set,
		CancellationToken cancellationToken = default)
	{
		var liveSet = await LiveSetAsync(set, cancellationToken);
		return await _bySet.GetOrLoadAsync(liveSet.Id, liveSet, async () =>
		{
			var loadedRows = await ReadUntilStableAsync(async () =>
			{
				var rows = await Reader.ReadAsync(context => context.Beatmaps
					.Include(b => b.Set)
					.Where(b => b.BeatmapsetId == liveSet.Id)
					.OrderBy(b => b.Id)
					.ToListAsync(cancellationToken), cancellationToken);
				var loaded = new List<BeatmapRecord>();
				foreach (var row in rows)
				{
					// A row whose deletion is still queued never comes back into memory.
					var stored = await ToBeatmapAsync(row, cancellationToken);
					if (!IsBeingRemoved(stored))
						loaded.Add(stored);
				}

				return loaded;
			});

			var beatmaps = ImmutableList.CreateBuilder<BeatmapRecord>();
			foreach (var beatmap in loadedRows.Select(Track))
			{
				Index(beatmap);
				beatmaps.Add(beatmap);
			}

			return beatmaps.ToImmutable();
		});
	}

	/// <inheritdoc />
	public async Task<Page<BeatmapRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (total, loadedRows) = await ReadUntilStableAsync<(int Total, List<BeatmapRecord> Rows)>(async () =>
		{
			var (count, rows) = await Reader.ReadAsync(async context =>
			{
				var matching = BeatmapQueryFilter.Apply(context.Beatmaps, query);
				var count = await matching.CountAsync(cancellationToken);
				var rows = await matching
					.Include(b => b.Set)
					.OrderByDescending(b => b.BeatmapsetId)
					.ThenBy(b => b.Star)
					.ThenBy(b => b.Id)
					.Skip(page.Offset)
					.Take(page.Limit)
					.ToListAsync(cancellationToken);
				return (count, rows);
			}, cancellationToken);

			var loaded = new List<BeatmapRecord>();
			foreach (var row in rows)
			{
				// A row whose deletion is still queued never comes back into memory.
				var stored = await ToBeatmapAsync(row, cancellationToken);
				if (!IsBeingRemoved(stored))
					loaded.Add(stored);
			}

			return (count, loaded);
		});

		var items = new List<BeatmapRecord>();
		foreach (var beatmap in loadedRows.Select(Track))
		{
			Index(beatmap);
			items.Add(beatmap);
		}

		return new Page<BeatmapRecord>(items, total);
	}

	/// <inheritdoc />
	public async Task RetainAsync(BeatmapsetRecord set, IReadOnlyCollection<BeatmapRecord> keep,
		CancellationToken cancellationToken = default)
	{
		var liveSet = await LiveSetAsync(set, cancellationToken);
		var setId = liveSet.Id;
		var keepIds = keep.Select(beatmap => beatmap.Id).ToArray();
		var keepSet = keepIds.ToHashSet();
		foreach (var beatmap in Items.Values.Where(beatmap =>
			         beatmap.Beatmapset.Id == setId && !keepSet.Contains(beatmap.Id)))
			RemoveFromMemory(beatmap);

		_retaining[setId] = keepSet;
		var retained = Writer.SaveAsync(Root.Beatmapset(setId), (GetType(), "retain", setId),
			new WriteCommand(async (context, token) =>
			{
				await context.Beatmaps
					.Where(b => b.BeatmapsetId == setId && !((IEnumerable<int>)keepIds).Contains(b.Id))
					.ExecuteDeleteAsync(token);
			}));
		_bySet.Change(setId, liveSet,
			current => [.. current.Where(beatmap => keepSet.Contains(beatmap.Id))],
			retained);
		_ = retained.ContinueWith(_ =>
		{
			NoteRemovalCommitted();
			_retaining.TryRemove(new KeyValuePair<int, HashSet<int>>(setId, keepSet));
		}, TaskScheduler.Default);
	}

	/// <summary>Stores the difficulties of a beatmapset as a scan or import brings them in.</summary>
	/// <param name="set">The beatmapset the difficulties belong to.</param>
	/// <param name="data">Every difficulty the set has, in order.</param>
	/// <param name="now">The time of the import, in UTC.</param>
	/// <param name="releasable">The ids of the sets whose difficulties this commit replaces or removes.</param>
	/// <param name="context">The EF context the commit runs on.</param>
	/// <param name="token">A token that cancels the commit.</param>
	/// <returns>Each stored difficulty, in the same order.</returns>
	/// <remarks>
	///     A difficulty already stored under the same file's hash, or under the same osu! id, is moved to this set and
	///     keeps its id, its creation time and whether it is locked or shown, when its set is replaced or removed by the
	///     same commit; otherwise it is refused.
	/// </remarks>
	internal static async Task<List<BeatmapRecord>> StoreAsync(BeatmapsetRecord set,
		IReadOnlyList<BeatmapData> data, DateTimeOffset now, IReadOnlySet<int> releasable, BasilDbContext context,
		CancellationToken token)
	{
		var stored = new List<BeatmapRecord>(data.Count);
		foreach (var item in data)
		{
			var row = await FindAsync(item, context, token);
			if (row is not null && row.BeatmapsetId != set.Id && !releasable.Contains(row.BeatmapsetId))
				throw new AlreadyExistsException(
					$"A difficulty of another beatmapset already has the hash {item.Hash} or the id {row.Id}.");

			var knownId = row?.Id ?? item.OnlineId;
			// A record needs an id; the row of a difficulty without an osu! id has none until the sequence gives it one.
			var toStore = ToRow(Build(knownId ?? 1));
			if (knownId is null)
				toStore.Id = 0;

			if (toStore.Id == 0)
			{
				// The database gives a difficulty without an osu! id the next local id.
				context.Beatmaps.Add(toStore);
				await context.SaveChangesAsync(token);
			}
			else
			{
				await context.UpsertAsync(toStore, candidate => candidate.Id == toStore.Id, token);
			}

			stored.Add(Build(toStore.Id));
			continue;

			BeatmapRecord Build(int id)
			{
				return new BeatmapRecord
				{
					Id = id,
					Beatmapset = set,
					Hash = item.Hash,
					Metadata = item.Metadata,
					Assets = item.Assets,
					Locked = row?.Locked ?? false,
					Visible = row?.Visible ?? true,
					CreatedAt = row?.CreatedAt ?? now,
					UpdatedAt = now
				};
			}
		}

		return stored;
	}

	private static async Task<BeatmapRow?> FindAsync(BeatmapData data, BasilDbContext context, CancellationToken token)
	{
		var hash = data.Hash.HashValue;
		var row = await context.Beatmaps.FirstOrDefaultAsync(b => b.Hash == hash, token);
		if (row is not null || data.OnlineId is not { } onlineId)
			return row;

		return await context.Beatmaps.FirstOrDefaultAsync(b => b.Id == onlineId, token);
	}

	/// <summary>Brings the live instance of a stored difficulty in step with the stored values.</summary>
	/// <param name="set">The live beatmapset the difficulty belongs to.</param>
	/// <param name="stored">The stored difficulty.</param>
	/// <returns>The live difficulty.</returns>
	internal BeatmapRecord Apply(BeatmapsetRecord set, BeatmapRecord stored)
	{
		var beatmap = Copy(stored, set);
		var live = Track(beatmap);
		if (ReferenceEquals(live, beatmap))
		{
			Index(live);
			return live;
		}

		Unindex(live);
		CopyTo(live, beatmap);
		Index(live);
		return live;
	}

	/// <summary>Replaces the difficulties a beatmapset has with the ones it is stored with.</summary>
	/// <param name="set">The live beatmapset.</param>
	/// <param name="beatmaps">Every difficulty the set is stored with, in order.</param>
	internal void Replace(BeatmapsetRecord set, IReadOnlyList<BeatmapRecord> beatmaps)
	{
		_bySet.Change(set.Id, set, _ => ImmutableList.CreateRange(beatmaps), Task.CompletedTask);
	}

	/// <summary>Forgets the stored difficulties of a removed beatmapset.</summary>
	/// <param name="id">The id of the removed beatmapset.</param>
	/// <param name="set">The live beatmapset that was removed.</param>
	internal void Forget(int id, BeatmapsetRecord set)
	{
		foreach (var beatmap in Items.Values.Where(beatmap => beatmap.Beatmapset.Id == id))
			RemoveFromMemory(beatmap);

		_bySet.Change(id, set, _ => ImmutableList<BeatmapRecord>.Empty, Task.CompletedTask);
	}

	protected override int KeyOf(BeatmapRecord item)
	{
		return item.Id;
	}

	protected override Root RootOf(BeatmapRecord item)
	{
		return Root.Beatmapset(item.Beatmapset.Id);
	}

	protected override void CopyTo(BeatmapRecord live, BeatmapRecord from)
	{
		live.Beatmapset = from.Beatmapset;
		live.Hash = from.Hash;
		live.Metadata = from.Metadata;
		live.Assets = from.Assets;
		live.Locked = from.Locked;
		live.Visible = from.Visible;
		live.UpdatedAt = from.UpdatedAt;
	}

	/// <summary>Gives the live instance of a beatmap the stored data and keeps the lookups in step.</summary>
	private void Remember(BeatmapRecord beatmap, BeatmapRecord value, Task committed)
	{
		var live = Track(beatmap);
		Unindex(live);
		if (!ReferenceEquals(live, value)) CopyTo(live, value);
		Index(live);
		_bySet.Change(value.Beatmapset.Id, value.Beatmapset, current => AddOrReplace(current, live), committed);
	}

	protected override async Task<BeatmapRecord?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync(context => context.Beatmaps
			.Include(b => b.Set)
			.FirstOrDefaultAsync(b => b.Id == key));
		return row is null ? null : await ToBeatmapAsync(row, CancellationToken.None);
	}

	protected override WriteCommand SaveCommand(BeatmapRecord beatmap)
	{
		var row = ToRow(beatmap);
		return new WriteCommand((context, token) => context.ReplaceAsync(row, stored => stored.Id == row.Id, token));
	}

	protected override bool IsBeingRemoved(BeatmapRecord beatmap)
	{
		return base.IsBeingRemoved(beatmap) ||
		       (_retaining.TryGetValue(beatmap.Beatmapset.Id, out var keep) && !keep.Contains(beatmap.Id));
	}

	private void RemoveFromMemory(BeatmapRecord beatmap)
	{
		Items.Remove(beatmap.Id);
		Unindex(beatmap);
	}

	private void Unindex(BeatmapRecord beatmap)
	{
		_byHash.Remove(beatmap.Hash, beatmap);
	}

	private void Index(BeatmapRecord beatmap)
	{
		_byHash.Set(beatmap.Hash, beatmap);
	}

	private static ImmutableList<BeatmapRecord> AddOrReplace(ImmutableList<BeatmapRecord> current,
		BeatmapRecord beatmap)
	{
		var index = current.FindIndex(item => item.Id == beatmap.Id);
		return index < 0 ? current.Add(beatmap) : current.SetItem(index, beatmap);
	}

	/// <summary>Gets the live instance of a beatmapset, or the given one when it is not stored.</summary>
	private async ValueTask<BeatmapsetRecord> LiveSetAsync(BeatmapsetRecord set, CancellationToken cancellationToken)
	{
		return await beatmapsets.GetAsync(set.Id, cancellationToken) ?? set;
	}

	/// <summary>Copies a beatmap's values into a new instance that belongs to a given beatmapset.</summary>
	private static BeatmapRecord Copy(BeatmapRecord beatmap, BeatmapsetRecord set)
	{
		return new BeatmapRecord
		{
			Id = beatmap.Id,
			Beatmapset = set,
			Hash = beatmap.Hash,
			Metadata = beatmap.Metadata,
			Assets = beatmap.Assets,
			Locked = beatmap.Locked,
			Visible = beatmap.Visible,
			CreatedAt = beatmap.CreatedAt,
			UpdatedAt = beatmap.UpdatedAt
		};
	}

	/// <summary>Builds a beatmap from a stored row, which holds its set.</summary>
	private async Task<BeatmapRecord> ToBeatmapAsync(BeatmapRow row, CancellationToken cancellationToken)
	{
		var set = await LiveSetAsync(row.Set.ToRecord(), cancellationToken);
		var objects = row.Objects.Deserialize<BeatmapObjects>(StoredJson)
		              ?? BeatmapObjects.Empty((GameMode)row.Mode);

		return new BeatmapRecord
		{
			Id = row.Id,
			Beatmapset = set,
			Hash = new Md5(row.Hash),
			Metadata = new BeatmapMetadata
			{
				Version = row.Version,
				Difficulty = new BeatmapDifficulty((GameMode)row.Mode, row.Bpm, TimeSpan.FromMilliseconds(row.Length),
					row.Cs, row.Ar, row.Od, row.Hp, row.Star),
				Objects = objects
			},
			Assets = new BeatmapAssets
			{
				HasStoryboard = row.Storyboard,
				BackgroundFile = row.Background,
				AudioFile = row.Audio,
				VideoFile = row.Video,
				PreviewTime = row.PreviewTime is { } preview ? TimeSpan.FromMilliseconds(preview) : null
			},
			Locked = row.Locked,
			Visible = row.Visible,
			CreatedAt = row.CreatedAt,
			UpdatedAt = row.UpdatedAt
		};
	}

	/// <summary>Makes the row that stores a beatmap, with the values it has now.</summary>
	private static BeatmapRow ToRow(BeatmapRecord beatmap)
	{
		var difficulty = beatmap.Metadata.Difficulty;
		return new BeatmapRow
		{
			Id = beatmap.Id,
			BeatmapsetId = beatmap.Beatmapset.Id,
			Hash = beatmap.Hash.HashValue,
			Version = beatmap.Metadata.Version,
			Mode = (int)difficulty.Mode,
			Star = difficulty.Star,
			Length = (int)difficulty.Length.TotalMilliseconds,
			Bpm = difficulty.Bpm,
			Cs = difficulty.Cs,
			Ar = difficulty.Ar,
			Od = difficulty.Od,
			Hp = difficulty.Hp,
			Objects = JsonSerializer.SerializeToElement(beatmap.Metadata.Objects, StoredJson),
			Background = beatmap.Assets.BackgroundFile,
			Audio = beatmap.Assets.AudioFile,
			Video = beatmap.Assets.VideoFile,
			PreviewTime = (int?)beatmap.Assets.PreviewTime?.TotalMilliseconds,
			Storyboard = beatmap.Assets.HasStoryboard,
			CreatedAt = beatmap.CreatedAt.ToUniversalTime(),
			UpdatedAt = beatmap.UpdatedAt.ToUniversalTime(),
			Locked = beatmap.Locked,
			Visible = beatmap.Visible
		};
	}

	/// <summary>Stores the objects of each game mode under the mode's own discriminator.</summary>
	/// <param name="info">The type being described.</param>
	private static void StoredObjects(JsonTypeInfo info)
	{
		if (info.Type != typeof(BeatmapObjects)) return;

		info.PolymorphismOptions = new JsonPolymorphismOptions
		{
			TypeDiscriminatorPropertyName = "mode",
			DerivedTypes =
			{
				new JsonDerivedType(typeof(OsuObjects), (int)GameMode.Standard),
				new JsonDerivedType(typeof(TaikoObjects), (int)GameMode.Taiko),
				new JsonDerivedType(typeof(CatchObjects), (int)GameMode.Catch),
				new JsonDerivedType(typeof(ManiaObjects), (int)GameMode.Mania)
			}
		};
	}
}