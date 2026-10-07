using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Storage.Batching;
using Basil.Infrastructure.Storage.Caching;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Beatmaps;

/// <summary>Stores beatmapsets in the <c>Beatmapsets</c> table.</summary>
internal sealed class SqliteBeatmapsetRepository(DatabaseBatcher batcher)
	: CachedRepository<int, Beatmapset>(batcher), IBeatmapsetRepository
{
	protected override int KeyOf(Beatmapset item) => item.Id;

	/// <inheritdoc />
	public ValueTask<Beatmapset?> GetAsync(int id, CancellationToken cancellationToken = default) => FindAsync(id, cancellationToken);

	/// <inheritdoc />
	public async Task<Beatmapset> CreateAsync(BeatmapsetData data, int? onlineId = null,
		CancellationToken cancellationToken = default)
	{
		var parameters = new DynamicParameters(new
		{
			id = 0,
			artist = data.Artist,
			title = data.Title,
			creator = data.Creator,
			createdAt = data.CreatedAt.ToUnixTimeMilliseconds(),
			updatedAt = data.UpdatedAt.ToUnixTimeMilliseconds(),
			locked = data.Locked ? 1 : 0,
			visible = data.Visible ? 1 : 0
		});
		var id = await Batcher.WriteAsync(async (connection, transaction) =>
		{
			var newId = onlineId ?? await NextLocalIdAsync(connection, transaction);
			parameters.Add("id", newId);
			await connection.ExecuteAsync(
				"""
				INSERT INTO Beatmapsets (Id, Artist, Title, Creator, CreatedAt, UpdatedAt, Locked, Visible)
				VALUES (@id, @artist, @title, @creator, @createdAt, @updatedAt, @locked, @visible)
				""",
				parameters,
				transaction);
			return newId;
		}, cancellationToken);

		return Track(new Beatmapset { Id = id, Value = data });
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		var live = Track(set);
		if (!ReferenceEquals(live, set))
		{
			live.Value.Artist = set.Value.Artist;
			live.Value.Title = set.Value.Title;
			live.Value.Creator = set.Value.Creator;
			live.Value.UpdatedAt = set.Value.UpdatedAt;
			live.Value.Locked = set.Value.Locked;
			live.Value.Visible = set.Value.Visible;
		}

		return SaveAsync(live);
	}

	/// <inheritdoc />
	public Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		return RemoveAsync(set);
	}

	/// <inheritdoc />
	public async Task<Page<Beatmapset>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var parameters = new DynamicParameters(new { limit = page.Limit, offset = page.Offset });
		var where = BeatmapQueryFilter.Build(query, parameters);
		var existsFilter = where.Length == 0 ? "" : $" AND {where}";

		var sql = $"""
		           SELECT s.Id, s.Artist, s.Title, s.Creator, s.CreatedAt, s.UpdatedAt, s.Locked, s.Visible
		           FROM Beatmapsets s
		           WHERE EXISTS (
		               SELECT 1 FROM Beatmaps b
		               WHERE b.BeatmapsetId = s.Id{existsFilter}
		           )
		           ORDER BY s.Id DESC
		           LIMIT @limit OFFSET @offset
		           """;

		var countSql = $"""
		                SELECT COUNT(*)
		                FROM Beatmapsets s
		                WHERE EXISTS (
		                    SELECT 1 FROM Beatmaps b
		                    WHERE b.BeatmapsetId = s.Id{existsFilter}
		                )
		                """;

		var (rows, total) = await Batcher.ReadAsync(async (connection, transaction) => (
			await connection.QueryAsync<BeatmapsetRow>(sql, parameters, transaction),
			await connection.ExecuteScalarAsync<int>(countSql, parameters, transaction)), cancellationToken);

		return new Page<Beatmapset>(rows.Select(row => Track(ToBeatmapset(row))).ToList(), total);
	}

	protected override async Task<Beatmapset?> LoadAsync(int key, CancellationToken cancellationToken)
	{
		var row = await Batcher.ReadAsync((connection, transaction) => connection.QuerySingleOrDefaultAsync<BeatmapsetRow>(
			"SELECT Id, Artist, Title, Creator, CreatedAt, UpdatedAt, Locked, Visible FROM Beatmapsets WHERE Id = @Id",
			new { Id = key }, transaction), cancellationToken);
		return row is null ? null : ToBeatmapset(row);
	}

	protected override string WriteSql =>
		"""
		INSERT INTO Beatmapsets (Id, Artist, Title, Creator, CreatedAt, UpdatedAt, Locked, Visible)
		VALUES (@Id, @Artist, @Title, @Creator, @CreatedAt, @UpdatedAt, @Locked, @Visible)
		ON CONFLICT(Id) DO UPDATE SET
			Artist = excluded.Artist,
			Title = excluded.Title,
			Creator = excluded.Creator,
			CreatedAt = excluded.CreatedAt,
			UpdatedAt = excluded.UpdatedAt,
			Locked = excluded.Locked,
			Visible = excluded.Visible;
		""";

	protected override object WriteParameters(Beatmapset set)
	{
		var value = set.Value;
		return new
		{
			set.Id,
			value.Artist,
			value.Title,
			value.Creator,
			CreatedAt = value.CreatedAt.ToUnixTimeMilliseconds(),
			UpdatedAt = value.UpdatedAt.ToUnixTimeMilliseconds(),
			Locked = value.Locked ? 1 : 0,
			Visible = value.Visible ? 1 : 0
		};
	}

	protected override Task EraseAsync(SqliteConnection connection, SqliteTransaction transaction, int key)
	{
		return connection.ExecuteAsync("DELETE FROM Beatmapsets WHERE Id = @Id", new { Id = key }, transaction);
	}

	private static async Task<int> NextLocalIdAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction)
	{
		var max = await connection.ExecuteScalarAsync<int?>(
			"SELECT COALESCE(MAX(Id), 0) FROM Beatmapsets", transaction: transaction);
		return Math.Max(Beatmapset.LocalIdFloor, (max ?? 0) + 1);
	}

	private static Beatmapset ToBeatmapset(BeatmapsetRow row)
	{
		return new Beatmapset
		{
			Id = (int)row.Id,
			Value = new BeatmapsetData
			{
				Artist = row.Artist,
				Title = row.Title,
				Creator = row.Creator,
				CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAt),
				UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAt),
				Locked = row.Locked != 0,
				Visible = row.Visible != 0
			}
		};
	}

	/// <summary>A stored row of the Beatmapsets table.</summary>
	private sealed class BeatmapsetRow
	{
		public long Id { get; set; }
		public string Artist { get; set; } = "";
		public string Title { get; set; } = "";
		public string Creator { get; set; } = "";
		public long CreatedAt { get; set; }
		public long UpdatedAt { get; set; }
		public long Locked { get; set; }
		public long Visible { get; set; }
	}
}
