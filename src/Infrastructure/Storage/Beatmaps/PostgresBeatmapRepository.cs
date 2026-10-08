using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Utilities;
using Basil.Infrastructure.Storage.Memory;
using Basil.Infrastructure.Storage.Writing;
using Dapper;
using Npgsql;

namespace Basil.Infrastructure.Storage.Beatmaps;

/// <summary>Stores beatmap difficulties in the <c>beatmaps</c> table.</summary>
internal sealed class PostgresBeatmapRepository : MemoryRepository<int, Beatmap>, IBeatmapRepository
{
	// jsonb keeps object keys in its own order, so the mode discriminator need not come first.
	private static readonly JsonSerializerOptions StoredJson = new() { AllowOutOfOrderMetadataProperties = true };

	private readonly IBeatmapsetRepository _beatmapsets;
	private readonly WeakIndex<Md5, Beatmap> _byHash = new();
	private readonly OwnedLists<int, Beatmapset, Beatmap> _bySet = new();

	// The beatmaps a set keeps while the deletion of its others is queued, by set id.
	private readonly ConcurrentDictionary<int, HashSet<int>> _retaining = new();

	public PostgresBeatmapRepository(Database database, DatabaseWriter writer, IBeatmapsetRepository beatmapsets)
		: base(database, writer)
	{
		_beatmapsets = beatmapsets;
	}

	protected override int KeyOf(Beatmap item) => item.Id;

	protected override Root RootOf(Beatmap item) => Root.Beatmapset(item.Value.Beatmapset.Id);

	/// <inheritdoc />
	public async Task<Beatmap> CreateAsync(BeatmapData data, int? onlineId = null,
		CancellationToken cancellationToken = default)
	{
		if (_byHash.TryGet(data.Hash, out var known) && known.Value.Hash == data.Hash)
			throw new AlreadyExistsException($"A beatmap already has the hash {data.Hash}.");
		if (onlineId is { } existingId && Items.TryGetValue(existingId, out _))
			throw new AlreadyExistsException($"A beatmap already has osu! id {onlineId}.");

		var set = await LiveSetAsync(data.Beatmapset, cancellationToken);
		var value = WithSet(data, set);
		var parameters = new DynamicParameters(Parameters(0, value));
		int id;
		try
		{
			id = await Writer.WriteAsync(Root.Beatmapset(set.Id), async (connection, transaction) =>
			{
				var newId = onlineId ?? await NextLocalIdAsync(connection, transaction);
				parameters.Add("Id", newId);
				await connection.ExecuteAsync(
					"""
					insert into beatmaps (id, beatmapset_id, hash, version, mode, star, length, bpm, cs, ar, od, hp, objects, locked, visible)
					values (@Id, @BeatmapsetId, @Hash, @Version, @Mode, @Star, @Length, @Bpm, @Cs, @Ar, @Od, @Hp, @Objects::jsonb, @Locked, @Visible)
					""",
					parameters, transaction);
				return newId;
			}, cancellationToken);
		}
		catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
		{
			throw new AlreadyExistsException(onlineId is not null
				? $"A beatmap already has osu! id {onlineId}."
				: $"A beatmap already has the hash {data.Hash}.");
		}

		var beatmap = Track(new Beatmap { Id = id, Value = value });
		Index(beatmap);
		_bySet.Change(set.Id, set, current => AddOrReplace(current, beatmap), Task.CompletedTask);
		return beatmap;
	}

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
	{
		var value = WithSet(beatmap.Value, await LiveSetAsync(beatmap.Value.Beatmapset, cancellationToken));
		if (!Items.TryGetValue(beatmap.Id, out var current) || current.Value.Hash != value.Hash)
		{
			if (_byHash.TryGet(value.Hash, out var known) && known.Id != beatmap.Id && known.Value.Hash == value.Hash)
				throw new AlreadyExistsException($"A beatmap already has the hash {value.Hash}.");

			// A hash must be unique across every beatmap, so a new or changed one is written at once and the live
			// beatmap takes it only after the commit.
			var id = beatmap.Id;
			var parameters = Parameters(id, value);
			try
			{
				await Writer.WriteAsync(Root.Beatmapset(value.Beatmapset.Id), (connection, transaction) =>
					connection.ExecuteAsync(WriteSql, parameters, transaction), cancellationToken);
			}
			catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
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

	protected override void CopyTo(Beatmap live, Beatmap from) => live.Value = from.Value;

	/// <summary>Gives the live instance of a beatmap the stored data and keeps the lookups in step.</summary>
	private void Remember(Beatmap beatmap, BeatmapData value, Task committed)
	{
		var live = Track(beatmap);
		Unindex(live);
		live.Value = value;
		Index(live);
		_bySet.Change(value.Beatmapset.Id, value.Beatmapset, current => AddOrReplace(current, live), committed);
	}

	/// <inheritdoc />
	public async ValueTask<Beatmap?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		var beatmap = await FindAsync(id, cancellationToken);
		if (beatmap is not null)
			Index(beatmap);
		return beatmap;
	}

	/// <inheritdoc />
	public async ValueTask<Beatmap?> GetAsync(Md5 hash, CancellationToken cancellationToken = default)
	{
		if (_byHash.TryGet(hash, out var known) && known.Value.Hash == hash)
			return known;

		var row = await Database.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<BeatmapRow>(
			ByHashSql, new { Hash = hash.HashValue }), cancellationToken);
		if (row is null)
			return null;

		var beatmap = Track(await ToBeatmapAsync(row, cancellationToken));
		Index(beatmap);
		return beatmap;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Beatmap>> ListAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		var liveSet = await LiveSetAsync(set, cancellationToken);
		return await _bySet.GetOrLoadAsync(liveSet.Id, liveSet, async () =>
		{
			var rows = await Database.ReadAsync(connection => connection.QueryAsync<BeatmapRow>(
				BySetSql, new { SetId = liveSet.Id }), cancellationToken);
			var beatmaps = ImmutableList.CreateBuilder<Beatmap>();
			foreach (var row in rows)
			{
				var beatmap = Track(await ToBeatmapAsync(row, cancellationToken));
				Index(beatmap);
				beatmaps.Add(beatmap);
			}

			return beatmaps.ToImmutable();
		});
	}

	/// <inheritdoc />
	public async Task<Page<Beatmap>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var parameters = new DynamicParameters(new { limit = page.Limit, offset = page.Offset });
		var where = BeatmapQueryFilter.Build(query, parameters);
		var filter = where.Length == 0 ? "" : $" WHERE {where}";
		var sql = $"""
		           select b.id, b.beatmapset_id, b.hash, b.version, b.mode, b.star, b.length, b.bpm, b.cs, b.ar, b.od, b.hp, b.objects, b.locked, b.visible,
		                  s.id as set_id, s.artist as set_artist, s.title as set_title, s.creator as set_creator, s.created_at as set_created_at, s.updated_at as set_updated_at, s.locked as set_locked, s.visible as set_visible
		           from beatmaps b
		           join beatmapsets s on s.id = b.beatmapset_id{filter}
		           order by b.beatmapset_id desc, b.star asc, b.id
		           limit @limit offset @offset
		           """;
		var countSql = $"""
		                select count(*)
		                from beatmaps b
		                join beatmapsets s on s.id = b.beatmapset_id{filter}
		                """;

		var (rows, total) = await Database.ReadAsync(async connection => (
			await connection.QueryAsync<BeatmapRow>(sql, parameters),
			await connection.ExecuteScalarAsync<int>(countSql, parameters)), cancellationToken);
		var items = new List<Beatmap>();
		foreach (var row in rows)
		{
			var beatmap = Track(await ToBeatmapAsync(row, cancellationToken));
			Index(beatmap);
			items.Add(beatmap);
		}

		return new Page<Beatmap>(items, total);
	}

	/// <inheritdoc />
	public async Task RetainAsync(Beatmapset set, IReadOnlyCollection<Beatmap> keep,
		CancellationToken cancellationToken = default)
	{
		var liveSet = await LiveSetAsync(set, cancellationToken);
		var setId = liveSet.Id;
		var keepIds = keep.Select(beatmap => beatmap.Id).ToArray();
		var keepSet = keepIds.ToHashSet();
		foreach (var beatmap in Items.Values.Where(beatmap =>
			         beatmap.Value.Beatmapset.Id == setId && !keepSet.Contains(beatmap.Id)))
			RemoveFromMemory(beatmap);

		_retaining[setId] = keepSet;
		var retained = Writer.SaveAsync(Root.Beatmapset(setId), (GetType(), "retain", setId), new WriteCommand(
			"delete from beatmaps where beatmapset_id = @SetId and id <> all(@KeepIds)",
			new { SetId = setId, KeepIds = keepIds }));
		_bySet.Change(setId, liveSet, current => current.Where(beatmap => keepSet.Contains(beatmap.Id)).ToImmutableList(),
			retained);
		_ = retained.ContinueWith(_ => _retaining.TryRemove(new KeyValuePair<int, HashSet<int>>(setId, keepSet)),
			TaskScheduler.Default);
	}

	protected override async Task<Beatmap?> LoadAsync(int key)
	{
		var row = await Database.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<BeatmapRow>(
			ByIdSql, new { Id = key }));
		return row is null ? null : await ToBeatmapAsync(row, default);
	}

	protected override string WriteSql => UpsertSql;

	protected override object WriteParameters(Beatmap beatmap) => Parameters(beatmap.Id, beatmap.Value);

	private const string UpsertSql = """
	                                 insert into beatmaps (id, beatmapset_id, hash, version, mode, star, length, bpm, cs, ar, od, hp, objects, locked, visible)
	                                 values (@Id, @BeatmapsetId, @Hash, @Version, @Mode, @Star, @Length, @Bpm, @Cs, @Ar, @Od, @Hp, @Objects::jsonb, @Locked, @Visible)
	                                 on conflict (id) do update set
	                                 	beatmapset_id = excluded.beatmapset_id,
	                                 	hash = excluded.hash,
	                                 	version = excluded.version,
	                                 	mode = excluded.mode,
	                                 	star = excluded.star,
	                                 	length = excluded.length,
	                                 	bpm = excluded.bpm,
	                                 	cs = excluded.cs,
	                                 	ar = excluded.ar,
	                                 	od = excluded.od,
	                                 	hp = excluded.hp,
	                                 	objects = excluded.objects,
	                                 	locked = excluded.locked,
	                                 	visible = excluded.visible;
	                                 """;

	protected override WriteCommand EraseCommand(int key)
	{
		return new WriteCommand("delete from beatmaps where id = @Id", new { Id = key });
	}

	private const string ByIdSql = """
	                               select b.id, b.beatmapset_id, b.hash, b.version, b.mode, b.star, b.length, b.bpm, b.cs, b.ar, b.od, b.hp, b.objects, b.locked, b.visible,
	                                      s.id as set_id, s.artist as set_artist, s.title as set_title, s.creator as set_creator, s.created_at as set_created_at, s.updated_at as set_updated_at, s.locked as set_locked, s.visible as set_visible
	                               from beatmaps b
	                               join beatmapsets s on s.id = b.beatmapset_id
	                               where b.id = @Id
	                               """;

	private const string ByHashSql = """
	                                 select b.id, b.beatmapset_id, b.hash, b.version, b.mode, b.star, b.length, b.bpm, b.cs, b.ar, b.od, b.hp, b.objects, b.locked, b.visible,
	                                        s.id as set_id, s.artist as set_artist, s.title as set_title, s.creator as set_creator, s.created_at as set_created_at, s.updated_at as set_updated_at, s.locked as set_locked, s.visible as set_visible
	                                 from beatmaps b
	                                 join beatmapsets s on s.id = b.beatmapset_id
	                                 where b.hash = @Hash
	                                 """;

	private const string BySetSql = """
	                                select b.id, b.beatmapset_id, b.hash, b.version, b.mode, b.star, b.length, b.bpm, b.cs, b.ar, b.od, b.hp, b.objects, b.locked, b.visible,
	                                       s.id as set_id, s.artist as set_artist, s.title as set_title, s.creator as set_creator, s.created_at as set_created_at, s.updated_at as set_updated_at, s.locked as set_locked, s.visible as set_visible
	                                from beatmaps b
	                                join beatmapsets s on s.id = b.beatmapset_id
	                                where b.beatmapset_id = @SetId
	                                order by b.id
	                                """;

	protected override bool IsBeingRemoved(Beatmap beatmap) =>
		base.IsBeingRemoved(beatmap) ||
		_retaining.TryGetValue(beatmap.Value.Beatmapset.Id, out var keep) && !keep.Contains(beatmap.Id);

	private void RemoveFromMemory(Beatmap beatmap)
	{
		Items.Remove(beatmap.Id);
		Unindex(beatmap);
	}

	private void Unindex(Beatmap beatmap)
	{
		_byHash.Remove(beatmap.Value.Hash, beatmap);
	}

	private void Index(Beatmap beatmap) => _byHash.Set(beatmap.Value.Hash, beatmap);

	private static ImmutableList<Beatmap> AddOrReplace(ImmutableList<Beatmap> current, Beatmap beatmap)
	{
		var index = current.FindIndex(item => item.Id == beatmap.Id);
		return index < 0 ? current.Add(beatmap) : current.SetItem(index, beatmap);
	}

	private static Task<int> NextLocalIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
	{
		return connection.ExecuteScalarAsync<int>("select nextval('local_beatmap_ids')::int", transaction: transaction);
	}

	private static object Parameters(int id, BeatmapData value)
	{
		return new
		{
			Id = id,
			BeatmapsetId = value.Beatmapset.Id,
			Hash = value.Hash.HashValue,
			value.Version,
			Mode = (int)value.Difficulty.Mode,
			value.Difficulty.Star,
			Length = (int)value.Difficulty.Length.TotalMilliseconds,
			value.Difficulty.Bpm,
			Cs = value.Difficulty.Cs,
			Ar = value.Difficulty.Ar,
			Od = value.Difficulty.Od,
			Hp = value.Difficulty.Hp,
			Objects = JsonSerializer.Serialize(value.Objects),
			Locked = value.Locked,
			Visible = value.Visible
		};
	}

	/// <summary>Gets the live instance of a beatmapset, or the given one when it is not stored.</summary>
	private async ValueTask<Beatmapset> LiveSetAsync(Beatmapset set, CancellationToken cancellationToken) =>
		await _beatmapsets.GetAsync(set.Id, cancellationToken) ?? set;

	private static BeatmapData WithSet(BeatmapData value, Beatmapset set)
	{
		return new BeatmapData
		{
			Hash = value.Hash,
			Beatmapset = set,
			Version = value.Version,
			Difficulty = value.Difficulty,
			Objects = value.Objects,
			Locked = value.Locked,
			Visible = value.Visible
		};
	}

	private async Task<Beatmap> ToBeatmapAsync(BeatmapRow row, CancellationToken cancellationToken)
	{
		var set = await LiveSetAsync(new Beatmapset
		{
			Id = row.SetId,
			Value = new BeatmapsetData
			{
				Artist = row.SetArtist,
				Title = row.SetTitle,
				Creator = row.SetCreator,
				CreatedAt = row.SetCreatedAt,
				UpdatedAt = row.SetUpdatedAt,
				Locked = row.SetLocked,
				Visible = row.SetVisible
			}
		}, cancellationToken);
		var objects = JsonSerializer.Deserialize<BeatmapObjects>(row.Objects, StoredJson)
		              ?? BeatmapObjects.NewFrom((GameMode)row.Mode);

		return new Beatmap
		{
			Id = row.Id,
			Value = new BeatmapData
			{
				Hash = new Md5(row.Hash),
				Beatmapset = set,
				Version = row.Version,
				Difficulty = new Difficulty(
					Mode: (GameMode)row.Mode,
					Bpm: row.Bpm,
					Length: TimeSpan.FromMilliseconds(row.Length),
					Cs: row.Cs,
					Ar: row.Ar,
					Od: row.Od,
					Hp: row.Hp,
					Star: row.Star),
				Objects = objects,
				Locked = row.Locked,
				Visible = row.Visible
			}
		};
	}

	/// <summary>A stored beatmap row joined with its beatmapset.</summary>
	private sealed class BeatmapRow
	{
		public int Id { get; set; }
		public int BeatmapsetId { get; set; }
		public string Hash { get; set; } = "";
		public string Version { get; set; } = "";
		public int Mode { get; set; }
		public double Star { get; set; }
		public int Length { get; set; }
		public double Bpm { get; set; }
		public double Cs { get; set; }
		public double Ar { get; set; }
		public double Od { get; set; }
		public double Hp { get; set; }
		public string Objects { get; set; } = "";
		public bool Locked { get; set; }
		public bool Visible { get; set; }
		public int SetId { get; set; }
		public string SetArtist { get; set; } = "";
		public string SetTitle { get; set; } = "";
		public string SetCreator { get; set; } = "";
		public DateTimeOffset SetCreatedAt { get; set; }
		public DateTimeOffset SetUpdatedAt { get; set; }
		public bool SetLocked { get; set; }
		public bool SetVisible { get; set; }
	}
}
