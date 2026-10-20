using Basil.Application.Storage.Beatmaps;
using Basil.Application.Storage.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Content;
using Basil.Infrastructure.Storage.Postgres;
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
	: MemoryRepository<int, BeatmapsetRecord>(reader, writer), IBeatmapsetRepository
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
	///     Gets the root a commit is stored under: it changes the rows of several beatmapsets, so it has no single root
	///     of its own and runs after every other commit.
	/// </summary>
	private static Root SyncRoot => Root.Beatmapset(0);

	/// <summary>Gets the repository that stores the difficulties the stored beatmapsets have.</summary>
	private PostgresBeatmapRepository Beatmaps =>
		(PostgresBeatmapRepository)services.GetRequiredService<IBeatmapRepository>();

	protected override string WriteSql => UpdateSql;

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
			var stored = await Writer.WriteAsync(SyncRoot, (connection, transaction) =>
				StoreAsync(changes, connection, transaction), cancellationToken).ConfigureAwait(false);
			return Remember(stored, changes.Remove);
		}
		catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
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

		return new Page<BeatmapsetRecord>(rows.Select(row => Track(ToBeatmapset(row))).ToList(), total);
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
	}

	protected override async Task<BeatmapsetRecord?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<BeatmapsetRow>(
			"""
			select id, artist, title, creator, created_at, updated_at, archive_hash, archive_size, archive_modified_at, storyboard, locked, visible
			from beatmapsets
			where id = @Id
			""", new { Id = key }));
		return row is null ? null : ToBeatmapset(row);
	}

	protected override object WriteParameters(BeatmapsetRecord set)
	{
		return RowParameters(set);
	}

	protected override WriteCommand EraseCommand(int key)
	{
		return new WriteCommand("delete from beatmapsets where id = @Id", new { Id = key });
	}

	/// <summary>Stores every change in one transaction, keeping what a replaced beatmapset was stored with.</summary>
	/// <param name="changes">The beatmapsets to store and to remove.</param>
	/// <param name="connection">The connection the transaction runs on.</param>
	/// <param name="transaction">The transaction every change is stored in.</param>
	/// <returns>What was stored, in the order of <see cref="BeatmapsetChanges.Store" />.</returns>
	private async Task<List<StoredSet>> StoreAsync(BeatmapsetChanges changes, NpgsqlConnection connection,
		NpgsqlTransaction transaction)
	{
		var now = time.GetUtcNow();
		var stored = new List<StoredSet>(changes.Store.Count);

		foreach (var item in changes.Store)
		{
			int id;
			DateTimeOffset createdAt;
			bool locked;
			bool visible;
			if (item.Existing is { } existing)
			{
				id = existing.Id;
				createdAt = existing.CreatedAt;
				locked = existing.Locked;
				visible = existing.Visible;
			}
			else
			{
				id = item.Data.OnlineId ?? await NextLocalIdAsync(connection, transaction).ConfigureAwait(false);
				createdAt = now;
				locked = false;
				visible = true;
			}

			var set = new BeatmapsetRecord
			{
				Id = id,
				Fingerprint = item.Data.Fingerprint,
				Metadata = item.Data.Metadata,
				Assets = item.Data.Assets,
				CreatedAt = createdAt,
				UpdatedAt = now,
				Locked = locked,
				Visible = visible
			};

			await connection.ExecuteAsync(item.Existing is null ? InsertSql : UpdateSql,
				RowParameters(set), transaction).ConfigureAwait(false);

			var beatmaps = await PostgresBeatmapRepository
				.StoreAsync(set, item.Data.Beatmaps, now, connection, transaction)
				.ConfigureAwait(false);

			await connection.ExecuteAsync(
					"delete from beatmaps where beatmapset_id = @Id and id <> all(@KeepIds)",
					new { Id = id, KeepIds = beatmaps.Select(beatmap => beatmap.Beatmap.Id).ToArray() }, transaction)
				.ConfigureAwait(false);

			stored.Add(new StoredSet(set, beatmaps));
		}

		foreach (var set in changes.Remove)
		{
			await connection.ExecuteAsync("delete from beatmaps where beatmapset_id = @Id", new { set.Id },
				transaction).ConfigureAwait(false);
			await connection.ExecuteAsync("delete from beatmapsets where id = @Id", new { set.Id }, transaction)
				.ConfigureAwait(false);
		}

		return stored;
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
			var difficulties = new List<BeatmapRecord>(item.Beatmaps.Count);
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
	private BeatmapsetRecord Live(BeatmapsetRecord set)
	{
		var live = Track(set);
		if (!ReferenceEquals(live, set)) CopyTo(live, set);
		return live;
	}

	private static object RowParameters(BeatmapsetRecord set)
	{
		return new
		{
			set.Id,
			set.Metadata.Artist,
			set.Metadata.Title,
			set.Metadata.Creator,
			CreatedAt = set.CreatedAt.ToUniversalTime(),
			UpdatedAt = set.UpdatedAt.ToUniversalTime(),
			ArchiveHash = set.Fingerprint.Hash.HashValue,
			ArchiveSize = set.Fingerprint.Stamp.Size,
			ArchiveModifiedAt = set.Fingerprint.Stamp.ModifiedAt.ToUniversalTime(),
			Storyboard = set.Assets.HasStoryboard,
			set.Locked,
			set.Visible
		};
	}

	private static Task<int> NextLocalIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
	{
		return connection.ExecuteScalarAsync<int>("select nextval('local_beatmapset_ids')::int",
			transaction: transaction);
	}

	private static BeatmapsetRecord ToBeatmapset(BeatmapsetRow row)
	{
		// A set stored before archives were recorded has no fingerprint; the next scan takes one, as for a changed archive.
		var fingerprint = row.ArchiveHash is { } hash
			? new ContentFingerprint(
				new ContentStamp(row.ArchiveSize ?? 0, row.ArchiveModifiedAt ?? DateTimeOffset.UnixEpoch),
				new Md5(hash))
			: default;
		return new BeatmapsetRecord
		{
			Id = row.Id,
			Fingerprint = fingerprint,
			Metadata = new BeatmapsetMetadata { Artist = row.Artist, Title = row.Title, Creator = row.Creator },
			Assets = new BeatmapsetAssets { HasStoryboard = row.Storyboard },
			CreatedAt = row.CreatedAt,
			UpdatedAt = row.UpdatedAt,
			Locked = row.Locked,
			Visible = row.Visible
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

/// <summary>One difficulty of a beatmapset, as a commit stored it.</summary>
/// <param name="Beatmap">The difficulty with the values it was stored with.</param>
internal sealed record StoredBeatmap(BeatmapRecord Beatmap);

/// <summary>One beatmapset, as a commit stored it.</summary>
/// <param name="Set">The beatmapset, with the values it now has.</param>
/// <param name="Beatmaps">Every difficulty the set now has, in order.</param>
internal sealed record StoredSet(BeatmapsetRecord Set, List<StoredBeatmap> Beatmaps);