using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Utilities;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Writing;
using Basil.Infrastructure.Storage.Memory;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Basil.Infrastructure.Storage.Beatmaps;

/// <summary>Stores beatmapsets in the <c>beatmapsets</c> table.</summary>
internal sealed class PostgresBeatmapsetRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	TimeProvider time,
	IServiceProvider services)
	: MemoryRepository<int, Beatmapset>(reader, writer), IBeatmapsetRepository
{
	private const string UpdateSql =
		"""
		update beatmapsets set
			artist = @Artist,
			title = @Title,
			creator = @Creator,
			created_at = @CreatedAt,
			updated_at = @UpdatedAt,
			archive_hash = @ArchiveHash,
			archive_size = @ArchiveSize,
			archive_modified_at = @ArchiveModifiedAt,
			storyboard = @Storyboard,
			locked = @Locked,
			visible = @Visible
		where id = @Id;
		""";

	private const string InsertSql =
		"""
		insert into beatmapsets (id, artist, title, creator, created_at, updated_at, archive_hash, archive_size, archive_modified_at, storyboard, locked, visible)
		values (@Id, @Artist, @Title, @Creator, @CreatedAt, @UpdatedAt, @ArchiveHash, @ArchiveSize, @ArchiveModifiedAt, @Storyboard, @Locked, @Visible);
		""";

	/// <summary>
	///     Gets the root a sync is stored under: it changes the rows of several beatmapsets, so it has no single root
	///     of its own and runs after every other sync.
	/// </summary>
	private static Root SyncRoot => Root.Beatmapset(0);

	/// <summary>Gets the repository that stores the difficulties the stored beatmapsets have.</summary>
	private PostgresBeatmapRepository Beatmaps =>
		(PostgresBeatmapRepository)services.GetRequiredService<IBeatmapRepository>();

	protected override string WriteSql => UpdateSql;

	/// <inheritdoc />
	public ValueTask<Beatmapset?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		return FindAsync(id, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Beatmapset> CreateAsync(BeatmapsetData data, int? onlineId = null,
		CancellationToken cancellationToken = default)
	{
		if (onlineId is { } existingId && Items.TryGetValue(existingId, out _))
			throw new AlreadyExistsException($"A beatmapset already has osu! id {existingId}.");

		var parameters = new DynamicParameters(RowParameters(0, data));
		int id;
		try
		{
			id = await Writer.WriteAsync(onlineId is { } given ? Root.Beatmapset(given) : Root.Server,
				async (connection, transaction) =>
				{
					var newId = onlineId ?? await NextLocalIdAsync(connection, transaction).ConfigureAwait(false);
					parameters.Add("Id", newId);
					await connection.ExecuteAsync(InsertSql, parameters, transaction).ConfigureAwait(false);
					return newId;
				}, cancellationToken).ConfigureAwait(false);
		}
		catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
		{
			throw new AlreadyExistsException(onlineId is { } givenId
				? $"A beatmapset already has osu! id {givenId}."
				: "A beatmapset already has this online id.");
		}

		return Track(new Beatmapset { Id = id, Value = data });
	}

	/// <inheritdoc />
	public Task UpdateAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		_ = SaveAsync(set);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Beatmapset>> SyncAsync(BeatmapsetSync sync,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var stored = await Writer.WriteAsync(SyncRoot, (connection, transaction) =>
				StoreAsync(sync, connection, transaction), cancellationToken).ConfigureAwait(false);
			return Remember(stored, sync.Remove);
		}
		catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
		{
			throw new AlreadyExistsException("A stored beatmapset or beatmap already has this id or hash.");
		}
	}

	/// <inheritdoc />
	public Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		_ = RemoveAsync(set);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<Page<Beatmapset>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var parameters = new DynamicParameters(new { limit = page.Limit, offset = page.Offset });
		var where = BeatmapQueryFilter.Build(query, parameters);
		var existsFilter = where.Length == 0 ? "" : $" AND {where}";

		var sql = $"""
		           select s.id, s.artist, s.title, s.creator, s.created_at, s.updated_at, s.archive_hash, s.archive_size, s.archive_modified_at, s.storyboard, s.locked, s.visible
		           from beatmapsets s
		           where exists (
		               select 1 from beatmaps b
		               where b.beatmapset_id = s.id{existsFilter}
		           )
		           order by s.id desc
		           limit @limit offset @offset
		           """;

		var countSql = $"""
		                select count(*)
		                from beatmapsets s
		                where exists (
		                    select 1 from beatmaps b
		                    where b.beatmapset_id = s.id{existsFilter}
		                )
		                """;

		var (rows, total) = await Reader.ReadAsync(async connection => (
			await connection.QueryAsync<BeatmapsetRow>(sql, parameters),
			await connection.ExecuteScalarAsync<int>(countSql, parameters)), cancellationToken);

		return new Page<Beatmapset>(rows.Select(row => Track(ToBeatmapset(row))).ToList(), total);
	}

	protected override int KeyOf(Beatmapset item)
	{
		return item.Id;
	}

	protected override Root RootOf(Beatmapset item)
	{
		return Root.Beatmapset(item.Id);
	}

	protected override void CopyTo(Beatmapset live, Beatmapset from)
	{
		live.Value.Artist = from.Value.Artist;
		live.Value.Title = from.Value.Title;
		live.Value.Creator = from.Value.Creator;
		live.Value.UpdatedAt = from.Value.UpdatedAt;
		live.Value.ArchiveHash = from.Value.ArchiveHash;
		live.Value.ArchiveSize = from.Value.ArchiveSize;
		live.Value.ArchiveModifiedAt = from.Value.ArchiveModifiedAt;
		live.Value.Storyboard = from.Value.Storyboard;
		live.Value.Locked = from.Value.Locked;
		live.Value.Visible = from.Value.Visible;
	}

	protected override async Task<Beatmapset?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<BeatmapsetRow>(
			"""
			select id, artist, title, creator, created_at, updated_at, archive_hash, archive_size, archive_modified_at, storyboard, locked, visible
			from beatmapsets
			where id = @Id
			""", new { Id = key }));
		return row is null ? null : ToBeatmapset(row);
	}

	protected override object WriteParameters(Beatmapset set)
	{
		return RowParameters(set.Id, set.Value);
	}

	protected override WriteCommand EraseCommand(int key)
	{
		return new WriteCommand("delete from beatmapsets where id = @Id", new { Id = key });
	}

	/// <summary>Stores every change of a sync in one transaction, keeping what a replaced beatmapset was stored with.</summary>
	/// <param name="sync">The beatmapsets to store and to remove.</param>
	/// <param name="connection">The connection the transaction runs on.</param>
	/// <param name="transaction">The transaction every change is stored in.</param>
	/// <returns>What was stored, in the order of <see cref="BeatmapsetSync.Store" />.</returns>
	private async Task<List<StoredSet>> StoreAsync(BeatmapsetSync sync, NpgsqlConnection connection,
		NpgsqlTransaction transaction)
	{
		var now = time.GetUtcNow();
		var stored = new List<StoredSet>(sync.Store.Count);

		foreach (var item in sync.Store)
		{
			int id;
			DateTimeOffset createdAt;
			bool locked;
			bool visible;
			if (item.Existing is { } existing)
			{
				id = existing.Id;
				createdAt = existing.Value.CreatedAt;
				locked = existing.Value.Locked;
				visible = existing.Value.Visible;
			}
			else
			{
				id = item.OnlineId ?? await NextLocalIdAsync(connection, transaction).ConfigureAwait(false);
				createdAt = now;
				locked = false;
				visible = true;
			}

			var set = new Beatmapset
			{
				Id = id,
				Value = new BeatmapsetData
				{
					Artist = item.Artist,
					Title = item.Title,
					Creator = item.Creator,
					CreatedAt = createdAt,
					UpdatedAt = now,
					ArchiveHash = item.ArchiveHash,
					ArchiveSize = item.ArchiveSize,
					ArchiveModifiedAt = item.ArchiveModifiedAt,
					Storyboard = item.Storyboard,
					Locked = locked,
					Visible = visible
				}
			};

			await connection.ExecuteAsync(item.Existing is null ? InsertSql : UpdateSql,
				RowParameters(id, set.Value), transaction).ConfigureAwait(false);

			var beatmaps = await PostgresBeatmapRepository.StoreAsync(set, item.Beatmaps, connection, transaction)
				.ConfigureAwait(false);

			await connection.ExecuteAsync(
				"delete from beatmaps where beatmapset_id = @Id and id <> all(@KeepIds)",
				new { Id = id, KeepIds = beatmaps.Select(beatmap => beatmap.Id).ToArray() }, transaction)
				.ConfigureAwait(false);

			stored.Add(new StoredSet(set, beatmaps));
		}

		foreach (var set in sync.Remove)
		{
			await connection.ExecuteAsync("delete from beatmaps where beatmapset_id = @Id", new { set.Id },
				transaction).ConfigureAwait(false);
			await connection.ExecuteAsync("delete from beatmapsets where id = @Id", new { set.Id }, transaction)
				.ConfigureAwait(false);
		}

		return stored;
	}

	/// <summary>Brings the stored beatmapsets in step in memory, once every change is stored.</summary>
	/// <param name="stored">What was stored, in the order of <see cref="BeatmapsetSync.Store" />.</param>
	/// <param name="removed">The beatmapsets that were removed.</param>
	/// <returns>The live beatmapsets, in the order of <see cref="BeatmapsetSync.Store" />.</returns>
	private List<Beatmapset> Remember(List<StoredSet> stored, IReadOnlyCollection<Beatmapset> removed)
	{
		var beatmaps = Beatmaps;
		var live = new List<Beatmapset>(stored.Count);

		foreach (var item in stored)
		{
			var set = Live(item.Set);
			var difficulties = new List<Beatmap>(item.Beatmaps.Count);
			foreach (var storedBeatmap in item.Beatmaps)
				difficulties.Add(beatmaps.Apply(set, storedBeatmap));
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
	private Beatmapset Live(Beatmapset set)
	{
		var live = Track(set);
		if (!ReferenceEquals(live, set)) CopyTo(live, set);
		return live;
	}

	private static object RowParameters(int id, BeatmapsetData data)
	{
		return new
		{
			Id = id,
			data.Artist,
			data.Title,
			data.Creator,
			CreatedAt = data.CreatedAt.ToUniversalTime(),
			UpdatedAt = data.UpdatedAt.ToUniversalTime(),
			ArchiveHash = data.ArchiveHash?.HashValue,
			data.ArchiveSize,
			ArchiveModifiedAt = data.ArchiveModifiedAt?.ToUniversalTime(),
			data.Storyboard,
			data.Locked,
			data.Visible
		};
	}

	private static Task<int> NextLocalIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
	{
		return connection.ExecuteScalarAsync<int>("select nextval('local_beatmapset_ids')::int",
			transaction: transaction);
	}

	private static Beatmapset ToBeatmapset(BeatmapsetRow row)
	{
		Md5? archiveHash = null;
		if (row.ArchiveHash is { } hash) archiveHash = new Md5(hash);
		return new Beatmapset
		{
			Id = row.Id,
			Value = new BeatmapsetData
			{
				Artist = row.Artist,
				Title = row.Title,
				Creator = row.Creator,
				CreatedAt = row.CreatedAt,
				UpdatedAt = row.UpdatedAt,
				ArchiveHash = archiveHash,
				ArchiveSize = row.ArchiveSize,
				ArchiveModifiedAt = row.ArchiveModifiedAt,
				Storyboard = row.Storyboard,
				Locked = row.Locked,
				Visible = row.Visible
			}
		};
	}

	/// <summary>A stored row of the <c>beatmapsets</c> table.</summary>
	private sealed class BeatmapsetRow
	{
		public int Id { get; set; }
		public string Artist { get; set; } = "";
		public string Title { get; set; } = "";
		public string Creator { get; set; } = "";
		public DateTimeOffset CreatedAt { get; set; }
		public DateTimeOffset UpdatedAt { get; set; }
		public string? ArchiveHash { get; set; }
		public long? ArchiveSize { get; set; }
		public DateTimeOffset? ArchiveModifiedAt { get; set; }
		public bool Storyboard { get; set; }
		public bool Locked { get; set; }
		public bool Visible { get; set; }
	}
}

/// <summary>One difficulty of a beatmapset, as a sync stored it.</summary>
/// <param name="Id">The stored id of the difficulty.</param>
/// <param name="Data">The stored values of the difficulty.</param>
/// <param name="Locked">Whether the stored difficulty is write-locked.</param>
/// <param name="Visible">Whether the stored difficulty is shown.</param>
internal sealed record StoredBeatmap(int Id, BeatmapSyncData Data, bool Locked, bool Visible);

/// <summary>One beatmapset, as a sync stored it.</summary>
/// <param name="Set">The beatmapset, with the values it now has.</param>
/// <param name="Beatmaps">Every difficulty the set now has, in order.</param>
internal sealed record StoredSet(Beatmapset Set, List<StoredBeatmap> Beatmaps);
