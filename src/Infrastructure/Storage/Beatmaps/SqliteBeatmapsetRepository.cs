using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Beatmaps;

/// <summary>Stores beatmapsets in the <c>Beatmapsets</c> table.</summary>
internal sealed class SqliteBeatmapsetRepository(Database database) : IBeatmapsetRepository
{
	/// <inheritdoc />
	public async ValueTask<Beatmapset?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var row = await connection.QuerySingleOrDefaultAsync<BeatmapsetRow>(
			"SELECT Id, Artist, Title, Creator, CreatedAt, UpdatedAt, Locked, Visible FROM Beatmapsets WHERE Id = @id",
			new { id });
		return row is null ? null : Map(row);
	}

	/// <inheritdoc />
	public async Task<Beatmapset> CreateAsync(BeatmapsetData data, int? onlineId = null,
		CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

		var id = onlineId ?? await NextLocalIdAsync(connection, transaction);

		await connection.ExecuteAsync(
			"""
			INSERT INTO Beatmapsets (Id, Artist, Title, Creator, CreatedAt, UpdatedAt, Locked, Visible)
			VALUES (@id, @artist, @title, @creator, @createdAt, @updatedAt, @locked, @visible)
			""",
			new
			{
				id,
				artist = data.Artist,
				title = data.Title,
				creator = data.Creator,
				createdAt = data.CreatedAt.ToUnixTimeMilliseconds(),
				updatedAt = data.UpdatedAt.ToUnixTimeMilliseconds(),
				locked = data.Locked ? 1 : 0,
				visible = data.Visible ? 1 : 0
			},
			transaction);

		await transaction.CommitAsync(cancellationToken);

		return new Beatmapset { Id = id, Value = data };
	}

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"""
			INSERT INTO Beatmapsets (Id, Artist, Title, Creator, CreatedAt, UpdatedAt, Locked, Visible)
			VALUES (@id, @artist, @title, @creator, @createdAt, @updatedAt, @locked, @visible)
			ON CONFLICT(Id) DO UPDATE SET
			    Artist    = excluded.Artist,
			    Title     = excluded.Title,
			    Creator   = excluded.Creator,
			    CreatedAt = excluded.CreatedAt,
			    UpdatedAt = excluded.UpdatedAt,
			    Locked    = excluded.Locked,
			    Visible   = excluded.Visible
			""",
			new
			{
				id = set.Id,
				artist = set.Value.Artist,
				title = set.Value.Title,
				creator = set.Value.Creator,
				createdAt = set.Value.CreatedAt.ToUnixTimeMilliseconds(),
				updatedAt = set.Value.UpdatedAt.ToUnixTimeMilliseconds(),
				locked = set.Value.Locked ? 1 : 0,
				visible = set.Value.Visible ? 1 : 0
			});
	}

	/// <inheritdoc />
	public async Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"DELETE FROM Beatmapsets WHERE Id = @id",
			new { id = set.Id });
	}

	/// <inheritdoc />
	public async Task<Page<Beatmapset>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
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

		var rows = await connection.QueryAsync<BeatmapsetRow>(sql, parameters);
		var total = await connection.ExecuteScalarAsync<int>(countSql, parameters);

		return new Page<Beatmapset>(rows.Select(Map).ToList(), total);
	}

	private static async Task<int> NextLocalIdAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction)
	{
		var max = await connection.ExecuteScalarAsync<int?>(
			"SELECT COALESCE(MAX(Id), 0) FROM Beatmapsets", transaction: transaction);
		return Math.Max(Beatmapset.LocalIdFloor, (max ?? 0) + 1);
	}

	private static Beatmapset Map(BeatmapsetRow row)
	{
		return new Beatmapset
		{
			Id = row.Id,
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

	private sealed record BeatmapsetRow(
		int Id,
		string Artist,
		string Title,
		string Creator,
		long CreatedAt,
		long UpdatedAt,
		long Locked,
		long Visible);
}
