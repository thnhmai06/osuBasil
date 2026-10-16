using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Content;
using Basil.Domain.Mechanics;
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
	// jsonb keeps object keys in its own order, so the mode discriminator need not come first.
	// The objects of each mode are stored with a mode discriminator; jsonb keeps object keys in its own order.
	private static readonly JsonSerializerOptions StoredJson = new()
	{
		AllowOutOfOrderMetadataProperties = true,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { StoredObjects } }
	};

	private readonly WeakIndex<Md5, Beatmap> _byHash = new();
	private readonly OwnedLists<int, Beatmapset, Beatmap> _bySet = new();

	// The beatmaps a set keeps while the deletion of its others is queued, by set id.
	private readonly ConcurrentDictionary<int, HashSet<int>> _retaining = new();

	protected override string WriteSql => UpdateSql;

	/// <inheritdoc />
	public async Task UpdateAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
	{
		var value = Copy(beatmap, await LiveSetAsync(beatmap.Beatmapset, cancellationToken).ConfigureAwait(false));
		if (!Items.TryGetValue(beatmap.Id, out var current) || current.Hash != value.Hash)
		{
			if (_byHash.TryGetValue(value.Hash, out var known) && known.Id != beatmap.Id &&
			    known.Hash == value.Hash)
				throw new AlreadyExistsException($"A beatmap already has the hash {value.Hash}.");

			// A hash must be unique across every beatmap, so a new or changed one is written at once and the live
			// beatmap takes it only after the commit.
			var id = beatmap.Id;
			var parameters = Parameters(value);
			try
			{
				await Writer.WriteAsync(Root.Beatmapset(value.Beatmapset.Id), (connection, transaction) =>
						connection.ExecuteAsync(WriteSql, parameters, transaction), cancellationToken)
					.ConfigureAwait(false);
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
		if (_byHash.TryGetValue(hash, out var known) && known.Hash == hash)
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
		           select b.id, b.beatmapset_id, b.hash, b.version, b.mode, b.star, b.length, b.bpm, b.cs, b.ar, b.od, b.hp, b.objects, b.background, b.audio, b.video, b.preview_time, b.storyboard, b.created_at, b.updated_at, b.locked, b.visible,
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
			         beatmap.Beatmapset.Id == setId && !keepSet.Contains(beatmap.Id)))
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

	/// <summary>Stores the difficulties of a beatmapset as a scan or import brings them in.</summary>
	/// <param name="set">The beatmapset the difficulties belong to.</param>
	/// <param name="data">Every difficulty the set has, in order.</param>
	/// <param name="now">The time of the import, in UTC.</param>
	/// <param name="connection">The connection the commit runs on.</param>
	/// <param name="transaction">The transaction the commit runs in.</param>
	/// <returns>Each stored difficulty, in the same order.</returns>
	/// <remarks>
	///     A difficulty already stored under the same file's hash, or under the same osu! id, is moved to this set and
	///     keeps its id, its creation time and whether it is locked or shown.
	/// </remarks>
	internal static async Task<List<StoredBeatmap>> StoreAsync(Beatmapset set,
		IReadOnlyList<BeatmapData> data,
		DateTimeOffset now, NpgsqlConnection connection, NpgsqlTransaction transaction)
	{
		var stored = new List<StoredBeatmap>(data.Count);
		foreach (var item in data)
		{
			var row = await FindAsync(item, connection, transaction).ConfigureAwait(false);
			var beatmap = new Beatmap
			{
				Id = row?.Id ?? item.OnlineId ?? await NextLocalIdAsync(connection, transaction).ConfigureAwait(false),
				Beatmapset = set,
				Hash = item.Hash,
				Metadata = item.Metadata,
				Assets = item.Assets,
				Locked = row?.Locked ?? false,
				Visible = row?.Visible ?? true,
				CreatedAt = row?.CreatedAt ?? now,
				UpdatedAt = now
			};

			await connection.ExecuteAsync(row is null ? InsertSql : UpdateSql, Parameters(beatmap), transaction)
				.ConfigureAwait(false);
			stored.Add(new StoredBeatmap(beatmap));
		}

		return stored;
	}

	/// <summary>Brings the live instance of a stored difficulty in step with the stored values.</summary>
	/// <param name="set">The live beatmapset the difficulty belongs to.</param>
	/// <param name="stored">The stored difficulty.</param>
	/// <returns>The live difficulty.</returns>
	internal Beatmap Apply(Beatmapset set, StoredBeatmap stored)
	{
		var beatmap = Copy(stored.Beatmap, set);
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
	internal void Replace(Beatmapset set, IReadOnlyList<Beatmap> beatmaps)
	{
		_bySet.Change(set.Id, set, _ => ImmutableList.CreateRange(beatmaps), Task.CompletedTask);
	}

	/// <summary>Forgets the stored difficulties of a removed beatmapset.</summary>
	/// <param name="id">The id of the removed beatmapset.</param>
	/// <param name="set">The live beatmapset that was removed.</param>
	internal void Forget(int id, Beatmapset set)
	{
		foreach (var beatmap in Items.Values.Where(beatmap => beatmap.Beatmapset.Id == id))
			RemoveFromMemory(beatmap);

		_bySet.Change(id, set, _ => ImmutableList<Beatmap>.Empty, Task.CompletedTask);
	}

	protected override int KeyOf(Beatmap item)
	{
		return item.Id;
	}

	protected override Root RootOf(Beatmap item)
	{
		return Root.Beatmapset(item.Beatmapset.Id);
	}

	protected override void CopyTo(Beatmap live, Beatmap from)
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
	private void Remember(Beatmap beatmap, Beatmap value, Task committed)
	{
		var live = Track(beatmap);
		Unindex(live);
		if (!ReferenceEquals(live, value)) CopyTo(live, value);
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
		return Parameters(beatmap);
	}

	protected override WriteCommand EraseCommand(int key)
	{
		return new WriteCommand("delete from beatmaps where id = @Id", new { Id = key });
	}

	protected override bool IsBeingRemoved(Beatmap beatmap)
	{
		return base.IsBeingRemoved(beatmap) ||
		       (_retaining.TryGetValue(beatmap.Beatmapset.Id, out var keep) && !keep.Contains(beatmap.Id));
	}

	private void RemoveFromMemory(Beatmap beatmap)
	{
		Items.Remove(beatmap.Id);
		Unindex(beatmap);
	}

	private void Unindex(Beatmap beatmap)
	{
		_byHash.Remove(beatmap.Hash, beatmap);
	}

	private void Index(Beatmap beatmap)
	{
		_byHash.Set(beatmap.Hash, beatmap);
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
	private static async Task<StoredBeatmapRow?> FindAsync(BeatmapData data,
		NpgsqlConnection connection,
		NpgsqlTransaction transaction)
	{
		var row = await connection.QuerySingleOrDefaultAsync<StoredBeatmapRow>(
			"select id, locked, visible, created_at from beatmaps where hash = @Hash",
			new { Hash = data.Hash.HashValue },
			transaction).ConfigureAwait(false);
		if (row is not null) return row;

		return data.OnlineId is { } onlineId
			? await connection.QuerySingleOrDefaultAsync<StoredBeatmapRow>(
					"select id, locked, visible, created_at from beatmaps where id = @Id", new { Id = onlineId },
					transaction)
				.ConfigureAwait(false)
			: null;
	}

	private static Task<int> NextLocalIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
	{
		return connection.ExecuteScalarAsync<int>("select nextval('local_beatmap_ids')::int", transaction: transaction);
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

	private static object Parameters(Beatmap beatmap)
	{
		var difficulty = beatmap.Metadata.Difficulty;
		return new
		{
			beatmap.Id,
			BeatmapsetId = beatmap.Beatmapset.Id,
			Hash = beatmap.Hash.HashValue,
			beatmap.Metadata.Version,
			Mode = (int)difficulty.Mode,
			difficulty.Star,
			Length = (int)difficulty.Length.TotalMilliseconds,
			difficulty.Bpm,
			difficulty.Cs,
			difficulty.Ar,
			difficulty.Od,
			difficulty.Hp,
			Objects = JsonSerializer.Serialize(beatmap.Metadata.Objects, StoredJson),
			Background = beatmap.Assets.BackgroundFile,
			Audio = beatmap.Assets.AudioFile,
			Video = beatmap.Assets.VideoFile,
			PreviewTime = (int?)beatmap.Assets.PreviewTime?.TotalMilliseconds,
			Storyboard = beatmap.Assets.HasStoryboard,
			CreatedAt = beatmap.CreatedAt.ToUniversalTime(),
			UpdatedAt = beatmap.UpdatedAt.ToUniversalTime(),
			beatmap.Locked,
			beatmap.Visible
		};
	}

	/// <summary>Gets the live instance of a beatmapset, or the given one when it is not stored.</summary>
	private async ValueTask<Beatmapset> LiveSetAsync(Beatmapset set, CancellationToken cancellationToken)
	{
		return await beatmapsets.GetAsync(set.Id, cancellationToken).ConfigureAwait(false) ?? set;
	}

	/// <summary>Copies a beatmap's values into a new instance that belongs to a given beatmapset.</summary>
	private static Beatmap Copy(Beatmap beatmap, Beatmapset set)
	{
		return new Beatmap
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

	private async Task<Beatmap> ToBeatmapAsync(BeatmapRow row, CancellationToken cancellationToken)
	{
		// A set stored before archives were recorded has no fingerprint; the next scan takes one, as for a changed archive.
		var fingerprint = row.SetArchiveHash is { } hash
			? new ContentFingerprint(
				new ContentStamp(row.SetArchiveSize ?? 0, row.SetArchiveModifiedAt ?? DateTimeOffset.UnixEpoch),
				new Md5(hash))
			: default;
		var set = await LiveSetAsync(new Beatmapset
		{
			Id = row.SetId,
			Fingerprint = fingerprint,
			Metadata =
				new BeatmapsetMetadata { Artist = row.SetArtist, Title = row.SetTitle, Creator = row.SetCreator },
			Assets = new BeatmapsetAssets { HasStoryboard = row.SetStoryboard },
			CreatedAt = row.SetCreatedAt,
			UpdatedAt = row.SetUpdatedAt,
			Locked = row.SetLocked,
			Visible = row.SetVisible
		}, cancellationToken).ConfigureAwait(false);
		var objects = JsonSerializer.Deserialize<BeatmapObjects>(row.Objects, StoredJson)
		              ?? BeatmapObjects.Empty((GameMode)row.Mode);

		return new Beatmap
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
		public bool Storyboard { get; set; }
		public DateTimeOffset CreatedAt { get; set; }
		public DateTimeOffset UpdatedAt { get; set; }
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

	/// <summary>The identity, lock, visibility and creation time of a stored row of the <c>beatmaps</c> table.</summary>
	private sealed class StoredBeatmapRow
	{
		public int Id { get; set; }
		public bool Locked { get; set; }
		public bool Visible { get; set; }
		public DateTimeOffset CreatedAt { get; set; }
	}

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
	                                 storyboard = @Storyboard,
	                                 created_at = @CreatedAt,
	                                 updated_at = @UpdatedAt,
	                                 locked = @Locked,
	                                 visible = @Visible
	                                 where id = @Id;
	                                 """;

	private const string InsertSql = """
	                                 insert into beatmaps (id, beatmapset_id, hash, version, mode, star, length, bpm, cs, ar, od, hp, objects, background, audio, video, preview_time, storyboard, created_at, updated_at, locked, visible)
	                                 values (@Id, @BeatmapsetId, @Hash, @Version, @Mode, @Star, @Length, @Bpm, @Cs, @Ar, @Od, @Hp, @Objects::jsonb, @Background, @Audio, @Video, @PreviewTime, @Storyboard, @CreatedAt, @UpdatedAt, @Locked, @Visible);
	                                 """;

	private const string ByIdSql = """
	                               select b.id, b.beatmapset_id, b.hash, b.version, b.mode, b.star, b.length, b.bpm, b.cs, b.ar, b.od, b.hp, b.objects, b.background, b.audio, b.video, b.preview_time, b.storyboard, b.created_at, b.updated_at, b.locked, b.visible,
	                               s.id as set_id, s.artist as set_artist, s.title as set_title, s.creator as set_creator, s.created_at as set_created_at, s.updated_at as set_updated_at, s.archive_hash as set_archive_hash, s.archive_size as set_archive_size, s.archive_modified_at as set_archive_modified_at, s.storyboard as set_storyboard, s.locked as set_locked, s.visible as set_visible
	                               from beatmaps b
	                               join beatmapsets s on s.id = b.beatmapset_id
	                               where b.id = @Id
	                               """;

	private const string ByHashSql = """
	                                 select b.id, b.beatmapset_id, b.hash, b.version, b.mode, b.star, b.length, b.bpm, b.cs, b.ar, b.od, b.hp, b.objects, b.background, b.audio, b.video, b.preview_time, b.storyboard, b.created_at, b.updated_at, b.locked, b.visible,
	                                 s.id as set_id, s.artist as set_artist, s.title as set_title, s.creator as set_creator, s.created_at as set_created_at, s.updated_at as set_updated_at, s.archive_hash as set_archive_hash, s.archive_size as set_archive_size, s.archive_modified_at as set_archive_modified_at, s.storyboard as set_storyboard, s.locked as set_locked, s.visible as set_visible
	                                 from beatmaps b
	                                 join beatmapsets s on s.id = b.beatmapset_id
	                                 where b.hash = @Hash
	                                 """;

	private const string BySetSql = """
	                                select b.id, b.beatmapset_id, b.hash, b.version, b.mode, b.star, b.length, b.bpm, b.cs, b.ar, b.od, b.hp, b.objects, b.background, b.audio, b.video, b.preview_time, b.storyboard, b.created_at, b.updated_at, b.locked, b.visible,
	                                s.id as set_id, s.artist as set_artist, s.title as set_title, s.creator as set_creator, s.created_at as set_created_at, s.updated_at as set_updated_at, s.archive_hash as set_archive_hash, s.archive_size as set_archive_size, s.archive_modified_at as set_archive_modified_at, s.storyboard as set_storyboard, s.locked as set_locked, s.visible as set_visible
	                                from beatmaps b
	                                join beatmapsets s on s.id = b.beatmapset_id
	                                where b.beatmapset_id = @SetId
	                                order by b.id
	                                """;

	#endregion
}