using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Utilities;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Writing;
using Basil.Infrastructure.Storage.Memory;
using Dapper;
using Npgsql;

namespace Basil.Infrastructure.Storage.Beatmaps;

/// <summary>Stores beatmap difficulties in the <c>beatmaps</c> table.</summary>
internal sealed class PostgresBeatmapRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IBeatmapsetRepository beatmapsets)
	: MemoryRepository<int, Beatmap>(reader, writer), IBeatmapRepository
{
	#region SQL Scripts

		private const string UpdateSql = """
		update beatmaps set
		beatmapset_id = @BeatmapsetId,
		hash = @Hash,
		version = @Version,
		mode = @Mode,
		star = @Star,
		length = @Length,
		bpm = @Bpm,
		cs = @Cs,
		ar = @Ar,
		od = @Od,
		hp = @Hp,
		objects = @Objects::jsonb,
		background = @Background,
		audio = @Audio,
		video = @Video,
		preview_time = @PreviewTime,
		locked = @Locked,
		visible = @Visible
		where id = @Id;
		""";

		private const string InsertSql = """
		insert into beatmaps (id, beatmapset_id, hash, version, mode, star, length, bpm, cs, ar, od, hp, objects, background, audio, video, preview_time, locked, visible)
		values (@Id, @BeatmapsetId, @Hash, @Version, @Mode, @Star, @Length, @Bpm, @Cs, @Ar, @Od, @Hp, @Objects::jsonb, @Background, @Audio, @Video, @PreviewTime, @Locked, @Visible);
		""";

		private const string ByIdSql = """
		select b.id, b.beatmapset_id, b.hash, b.version, b.mode, b.star, b.length, b.bpm, b.cs, b.ar, b.od, b.hp, b.objects, b.background, b.audio, b.video, b.preview_time, b.locked, b.visible,
		s.id as set_id, s.artist as set_artist, s.title as set_title, s.creator as set_creator, s.created_at as set_created_at, s.updated_at as set_updated_at, s.archive_hash as set_archive_hash, s.archive_size as set_archive_size, s.archive_modified_at as set_archive_modified_at, s.storyboard as set_storyboard, s.locked as set_locked, s.visible as set_visible
		from beatmaps b
		join beatmapsets s on s.id = b.beatmapset_id
		where b.id = @Id
		""";

		private const string ByHashSql = """
		select b.id, b.beatmapset_id, b.hash, b.version, b.mode, b.star, b.length, b.bpm, b.cs, b.ar, b.od, b.hp, b.objects, b.background, b.audio, b.video, b.preview_time, b.locked, b.visible,
		s.id as set_id, s.artist as set_artist, s.title as set_title, s.creator as set_creator, s.created_at as set_created_at, s.updated_at as set_updated_at, s.archive_hash as set_archive_hash, s.archive_size as set_archive_size, s.archive_modified_at as set_archive_modified_at, s.storyboard as set_storyboard, s.locked as set_locked, s.visible as set_visible
		from beatmaps b
		join beatmapsets s on s.id = b.beatmapset_id
		where b.hash = @Hash
		""";

		private const string BySetSql = """
		select b.id, b.beatmapset_id, b.hash, b.version, b.mode, b.star, b.length, b.bpm, b.cs, b.ar, b.od, b.hp, b.objects, b.background, b.audio, b.video, b.preview_time, b.locked, b.visible,
		s.id as set_id, s.artist as set_artist, s.title as set_title, s.creator as set_creator, s.created_at as set_created_at, s.updated_at as set_updated_at, s.archive_hash as set_archive_hash, s.archive_size as set_archive_size, s.archive_modified_at as set_archive_modified_at, s.storyboard as set_storyboard, s.locked as set_locked, s.visible as set_visible
		from beatmaps b
		join beatmapsets s on s.id = b.beatmapset_id
		where b.beatmapset_id = @SetId
		order by b.id
		""";

	#endregion

	// jsonb keeps object keys in its own order, so the mode discriminator need not come first.
	private static readonly JsonSerializerOptions StoredJson = new() { AllowOutOfOrderMetadataProperties = true };

	private readonly WeakIndex<Md5, Beatmap> _byHash = new();
	private readonly OwnedLists<int, Beatmapset, Beatmap> _bySet = new();

	// The beatmaps a set keeps while the deletion of its others is queued, by set id.
	private readonly ConcurrentDictionary<int, HashSet<int>> _retaining = new();

	protected override string WriteSql => UpdateSql;

	/// <inheritdoc />
	public async Task<Beatmap> CreateAsync(BeatmapData data, int? onlineId = null,
		CancellationToken cancellationToken = default)
	{
		if (_byHash.TryGetValue(data.Hash, out var known) && known.Value.Hash == data.Hash)
			throw new AlreadyExistsException($"A beatmap already has the hash {data.Hash}.");
		if (onlineId is { } existingId && Items.TryGetValue(existingId, out _))
			throw new AlreadyExistsException($"A beatmap already has osu! id {onlineId}.");

		var set = await LiveSetAsync(data.Beatmapset, cancellationToken).ConfigureAwait(false);
		var value = WithSet(data, set);
		var parameters = new DynamicParameters(Parameters(0, value));
		int id;
		try
		{
			id = await Writer.WriteAsync(Root.Beatmapset(set.Id), async (connection, transaction) =>
			{
				var newId = onlineId ?? await NextLocalIdAsync(connection, transaction).ConfigureAwait(false);
				parameters.Add("Id", newId);
				await connection.ExecuteAsync(InsertSql, parameters, transaction).ConfigureAwait(false);
				return newId;
			}, cancellationToken).ConfigureAwait(false);
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
	public async Task UpdateAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
	{
		var value = WithSet(beatmap.Value, await LiveSetAsync(beatmap.Value.Beatmapset, cancellationToken).ConfigureAwait(false));
		if (!Items.TryGetValue(beatmap.Id, out var current) || current.Value.Hash != value.Hash)
		{
			if (_byHash.TryGetValue(value.Hash, out var known) && known.Id != beatmap.Id &&
			    known.Value.Hash == value.Hash)
				throw new AlreadyExistsException($"A beatmap already has the hash {value.Hash}.");

			// A hash must be unique across every beatmap, so a new or changed one is written at once and the live
			// beatmap takes it only after the commit.
			var id = beatmap.Id;
			var parameters = Parameters(id, value);
			try
			{
				await Writer.WriteAsync(Root.Beatmapset(value.Beatmapset.Id), (connection, transaction) =>
					connection.ExecuteAsync(WriteSql, parameters, transaction), cancellationToken).ConfigureAwait(false);
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

	/// <inheritdoc />
	public async ValueTask<Beatmap?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		var beatmap = await FindAsync(id, cancellationToken).ConfigureAwait(false);
		if (beatmap is not null)
			Index(beatmap);
		return beatmap;
	}

	/// <inheritdoc />
	public async ValueTask<Beatmap?> GetAsync(Md5 hash, CancellationToken cancellationToken = default)
	{
		if (_byHash.TryGetValue(hash, out var known) && known.Value.Hash == hash)
			return known;

		var row = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<BeatmapRow>(
			ByHashSql, new { Hash = hash.HashValue }), cancellationToken).ConfigureAwait(false);
		if (row is null)
			return null;

		var beatmap = Track(await ToBeatmapAsync(row, cancellationToken).ConfigureAwait(false));
		Index(beatmap);
		return beatmap;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Beatmap>> ListAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		var liveSet = await LiveSetAsync(set, cancellationToken).ConfigureAwait(false);
		return await _bySet.GetOrLoadAsync(liveSet.Id, liveSet, async () =>
		{
			var rows = await Reader.ReadAsync(connection => connection.QueryAsync<BeatmapRow>(
				BySetSql, new { SetId = liveSet.Id }), cancellationToken).ConfigureAwait(false);
			var beatmaps = ImmutableList.CreateBuilder<Beatmap>();
			foreach (var row in rows)
			{
				var beatmap = Track(await ToBeatmapAsync(row, cancellationToken).ConfigureAwait(false));
				Index(beatmap);
				beatmaps.Add(beatmap);
			}

			return beatmaps.ToImmutable();
		}).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<Page<Beatmap>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var parameters = new DynamicParameters(new { limit = page.Limit, offset = page.Offset });
		var where = BeatmapQueryFilter.Build(query, parameters);
		var filter = where.Length == 0 ? "" : $" WHERE {where}";
		var sql = $"""
		           select b.id, b.beatmapset_id, b.hash, b.version, b.mode, b.star, b.length, b.bpm, b.cs, b.ar, b.od, b.hp, b.objects, b.background, b.audio, b.video, b.preview_time, b.locked, b.visible,
		                  s.id as set_id, s.artist as set_artist, s.title as set_title, s.creator as set_creator, s.created_at as set_created_at, s.updated_at as set_updated_at, s.archive_hash as set_archive_hash, s.archive_size as set_archive_size, s.archive_modified_at as set_archive_modified_at, s.storyboard as set_storyboard, s.locked as set_locked, s.visible as set_visible
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

		var (rows, total) = await Reader.ReadAsync(async connection => (
			await connection.QueryAsync<BeatmapRow>(sql, parameters),
			await connection.ExecuteScalarAsync<int>(countSql, parameters)), cancellationToken).ConfigureAwait(false);
		var items = new List<Beatmap>();
		foreach (var row in rows)
		{
			var beatmap = Track(await ToBeatmapAsync(row, cancellationToken).ConfigureAwait(false));
			Index(beatmap);
			items.Add(beatmap);
		}

		return new Page<Beatmap>(items, total);
	}

	/// <inheritdoc />
	public async Task RetainAsync(Beatmapset set, IReadOnlyCollection<Beatmap> keep,
		CancellationToken cancellationToken = default)
	{
		var liveSet = await LiveSetAsync(set, cancellationToken).ConfigureAwait(false);
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
		_bySet.Change(setId, liveSet,
			current => [.. current.Where(beatmap => keepSet.Contains(beatmap.Id))],
			retained);
		_ = retained.ContinueWith(_ => _retaining.TryRemove(new KeyValuePair<int, HashSet<int>>(setId, keepSet)),
			TaskScheduler.Default);
	}

	/// <summary>Stores the difficulties of a beatmapset as a scan brings them in.</summary>
	/// <param name="set">The beatmapset the difficulties belong to.</param>
	/// <param name="data">Every difficulty the set has, in order.</param>
	/// <param name="connection">The connection the scan runs on.</param>
	/// <param name="transaction">The transaction the scan runs in.</param>
	/// <returns>Each stored difficulty, in the same order.</returns>
	/// <remarks>
	///     A difficulty already stored under the same file's hash, or under the same osu! id, is moved to this set and
	///     keeps its id and whether it is locked or shown.
	/// </remarks>
	internal static async Task<List<StoredBeatmap>> StoreAsync(Beatmapset set, IReadOnlyList<BeatmapSyncData> data,
		NpgsqlConnection connection, NpgsqlTransaction transaction)
	{
		var stored = new List<StoredBeatmap>(data.Count);
		foreach (var item in data)
		{
			var row = await FindAsync(item, connection, transaction).ConfigureAwait(false);
			var id = row?.Id ?? item.OnlineId ??
			         await NextLocalIdAsync(connection, transaction).ConfigureAwait(false);
			var locked = row?.Locked ?? false;
			var visible = row?.Visible ?? true;
			var value = new BeatmapData
			{
				Hash = item.Hash,
				Beatmapset = set,
				Version = item.Version,
				Difficulty = item.Difficulty,
				Objects = item.Objects,
				Background = item.Background,
				Audio = item.Audio,
				Video = item.Video,
				PreviewTime = item.PreviewTime,
				Locked = locked,
				Visible = visible
			};

			await connection.ExecuteAsync(row is null ? InsertSql : UpdateSql, Parameters(id, value), transaction)
				.ConfigureAwait(false);
			stored.Add(new StoredBeatmap(id, item, locked, visible));
		}

		return stored;
	}

	/// <summary>Brings the live instance of a stored difficulty in step with the stored values.</summary>
	/// <param name="set">The live beatmapset the difficulty belongs to.</param>
	/// <param name="stored">The stored difficulty.</param>
	/// <returns>The live difficulty.</returns>
	/// <remarks>A difficulty whose file changed forgets the content it had understood.</remarks>
	internal Beatmap Apply(Beatmapset set, StoredBeatmap stored)
	{
		var beatmap = new Beatmap
		{
			Id = stored.Id,
			Value = new BeatmapData
			{
				Hash = stored.Data.Hash,
				Beatmapset = set,
				Version = stored.Data.Version,
				Difficulty = stored.Data.Difficulty,
				Objects = stored.Data.Objects,
				Background = stored.Data.Background,
				Audio = stored.Data.Audio,
				Video = stored.Data.Video,
				PreviewTime = stored.Data.PreviewTime,
				Locked = stored.Locked,
				Visible = stored.Visible
			}
		};

		var live = Track(beatmap);
		if (ReferenceEquals(live, beatmap))
		{
			Index(live);
			return live;
		}

		Unindex(live);
		if (live.Value.Hash != stored.Data.Hash) live.Content = null;
		live.Value = beatmap.Value;
		Index(live);
		return live;
	}

	/// <summary>Replaces the difficulties a beatmapset has with the ones it is stored with.</summary>
	/// <param name="set">The live beatmapset.</param>
	/// <param name="beatmaps">Every difficulty the set is stored with, in order.</param>
	internal void Replace(Beatmapset set, IReadOnlyList<Beatmap> beatmaps)
	{
		_bySet.Change(set.Id, set, _ => ImmutableList.CreateRange(beatmaps), Task.CompletedTask);
	}

	/// <summary>Forgets the stored difficulties of a removed beatmapset.</summary>
	/// <param name="id">The id of the removed beatmapset.</param>
	/// <param name="set">The live beatmapset that was removed.</param>
	internal void Forget(int id, Beatmapset set)
	{
		foreach (var beatmap in Items.Values.Where(beatmap => beatmap.Value.Beatmapset.Id == id))
			RemoveFromMemory(beatmap);

		_bySet.Change(id, set, _ => ImmutableList<Beatmap>.Empty, Task.CompletedTask);
	}

	protected override int KeyOf(Beatmap item)
	{
		return item.Id;
	}

	protected override Root RootOf(Beatmap item)
	{
		return Root.Beatmapset(item.Value.Beatmapset.Id);
	}

	protected override void CopyTo(Beatmap live, Beatmap from)
	{
		live.Value = from.Value;
	}

	/// <summary>Gives the live instance of a beatmap the stored data and keeps the lookups in step.</summary>
	private void Remember(Beatmap beatmap, BeatmapData value, Task committed)
	{
		var live = Track(beatmap);
		Unindex(live);
		live.Value = value;
		Index(live);
		_bySet.Change(value.Beatmapset.Id, value.Beatmapset, current => AddOrReplace(current, live), committed);
	}

	protected override async Task<Beatmap?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<BeatmapRow>(
			ByIdSql, new { Id = key })).ConfigureAwait(false);
		return row is null ? null : await ToBeatmapAsync(row, default).ConfigureAwait(false);
	}

	protected override object WriteParameters(Beatmap beatmap)
	{
		return Parameters(beatmap.Id, beatmap.Value);
	}

	protected override WriteCommand EraseCommand(int key)
	{
		return new WriteCommand("delete from beatmaps where id = @Id", new { Id = key });
	}

	protected override bool IsBeingRemoved(Beatmap beatmap)
	{
		return base.IsBeingRemoved(beatmap) ||
		       (_retaining.TryGetValue(beatmap.Value.Beatmapset.Id, out var keep) && !keep.Contains(beatmap.Id));
	}

	private void RemoveFromMemory(Beatmap beatmap)
	{
		Items.Remove(beatmap.Id);
		Unindex(beatmap);
	}

	private void Unindex(Beatmap beatmap)
	{
		_byHash.Remove(beatmap.Value.Hash, beatmap);
	}

	private void Index(Beatmap beatmap)
	{
		_byHash.Set(beatmap.Value.Hash, beatmap);
	}

	private static ImmutableList<Beatmap> AddOrReplace(ImmutableList<Beatmap> current, Beatmap beatmap)
	{
		var index = current.FindIndex(item => item.Id == beatmap.Id);
		return index < 0 ? current.Add(beatmap) : current.SetItem(index, beatmap);
	}

	/// <summary>Finds the stored row of a difficulty, by the hash of its file or by its osu! id.</summary>
	/// <param name="data">The difficulty to find.</param>
	/// <param name="connection">The connection the sync runs on.</param>
	/// <param name="transaction">The transaction the sync runs in.</param>
	/// <returns>The stored row, or <see langword="null" /> when the difficulty is not stored yet.</returns>
	private static async Task<StoredBeatmapRow?> FindAsync(BeatmapSyncData data, NpgsqlConnection connection,
		NpgsqlTransaction transaction)
	{
		var row = await connection.QuerySingleOrDefaultAsync<StoredBeatmapRow>(
			"select id, locked, visible from beatmaps where hash = @Hash", new { Hash = data.Hash.HashValue },
			transaction).ConfigureAwait(false);
		if (row is not null) return row;

		return data.OnlineId is { } onlineId
			? await connection.QuerySingleOrDefaultAsync<StoredBeatmapRow>(
				"select id, locked, visible from beatmaps where id = @Id", new { Id = onlineId }, transaction)
				.ConfigureAwait(false)
			: null;
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
			value.Difficulty.Cs,
			value.Difficulty.Ar,
			value.Difficulty.Od,
			value.Difficulty.Hp,
			Objects = JsonSerializer.Serialize(value.Objects),
			value.Background,
			value.Audio,
			value.Video,
			PreviewTime = (int?)value.PreviewTime?.TotalMilliseconds,
			value.Locked,
			value.Visible
		};
	}

	/// <summary>Gets the live instance of a beatmapset, or the given one when it is not stored.</summary>
	private async ValueTask<Beatmapset> LiveSetAsync(Beatmapset set, CancellationToken cancellationToken)
	{
		return await beatmapsets.GetAsync(set.Id, cancellationToken).ConfigureAwait(false) ?? set;
	}

	private static BeatmapData WithSet(BeatmapData value, Beatmapset set)
	{
		return new BeatmapData
		{
			Hash = value.Hash,
			Beatmapset = set,
			Version = value.Version,
			Difficulty = value.Difficulty,
			Objects = value.Objects,
			Background = value.Background,
			Audio = value.Audio,
			Video = value.Video,
			PreviewTime = value.PreviewTime,
			Locked = value.Locked,
			Visible = value.Visible
		};
	}

	private async Task<Beatmap> ToBeatmapAsync(BeatmapRow row, CancellationToken cancellationToken)
	{
		Md5? archiveHash = null;
		if (row.SetArchiveHash is { } hash) archiveHash = new Md5(hash);
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
				ArchiveHash = archiveHash,
				ArchiveSize = row.SetArchiveSize,
				ArchiveModifiedAt = row.SetArchiveModifiedAt,
				Storyboard = row.SetStoryboard,
				Locked = row.SetLocked,
				Visible = row.SetVisible
			}
		}, cancellationToken).ConfigureAwait(false);
		var objects = JsonSerializer.Deserialize<BeatmapObjects>(row.Objects, StoredJson)
		              ?? BeatmapObjects.Empty((GameMode)row.Mode);

		return new Beatmap
		{
			Id = row.Id,
			Value = new BeatmapData
			{
				Hash = new Md5(row.Hash),
				Beatmapset = set,
				Version = row.Version,
				Difficulty = new Difficulty(
					(GameMode)row.Mode,
					row.Bpm,
					TimeSpan.FromMilliseconds(row.Length),
					row.Cs,
					row.Ar,
					row.Od,
					row.Hp,
					row.Star),
				Objects = objects,
				Background = row.Background,
				Audio = row.Audio,
				Video = row.Video,
				PreviewTime = row.PreviewTime is { } preview ? TimeSpan.FromMilliseconds(preview) : null,
				Locked = row.Locked,
				Visible = row.Visible
			}
		};
	}

	/// <summary>A stored row of the <c>beatmaps</c> table joined with its beatmapset.</summary>
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
		public string? Background { get; set; }
		public string? Audio { get; set; }
		public string? Video { get; set; }
		public int? PreviewTime { get; set; }
		public bool Locked { get; set; }
		public bool Visible { get; set; }
		public int SetId { get; set; }
		public string SetArtist { get; set; } = "";
		public string SetTitle { get; set; } = "";
		public string SetCreator { get; set; } = "";
		public DateTimeOffset SetCreatedAt { get; set; }
		public DateTimeOffset SetUpdatedAt { get; set; }
		public string? SetArchiveHash { get; set; }
		public long? SetArchiveSize { get; set; }
		public DateTimeOffset? SetArchiveModifiedAt { get; set; }
		public bool SetStoryboard { get; set; }
		public bool SetLocked { get; set; }
		public bool SetVisible { get; set; }
	}

	/// <summary>The identity, lock and visibility of a stored row of the <c>beatmaps</c> table.</summary>
	private sealed class StoredBeatmapRow
	{
		public int Id { get; set; }
		public bool Locked { get; set; }
		public bool Visible { get; set; }
	}
}
