using System.Text.Json;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Utilities;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Beatmaps;

/// <summary>Stores beatmap difficulties in the <c>Beatmaps</c> table.</summary>
internal sealed class SqliteBeatmapRepository(Database database) : IBeatmapRepository
{
	/// <inheritdoc />
	public async Task<Beatmap> CreateAsync(BeatmapData data, int? onlineId = null,
		CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

		var id = onlineId ?? await NextLocalIdAsync(connection, transaction);

		await connection.ExecuteAsync(
			"""
			INSERT INTO Beatmaps (Id, BeatmapsetId, Hash, Version, Mode, Star, Length, Bpm, Cs, Ar, Od, Hp, Objects, Locked, Visible)
			VALUES (@id, @beatmapsetId, @hash, @version, @mode, @star, @length, @bpm, @cs, @ar, @od, @hp, @objects, @locked, @visible)
			""",
			new
			{
				id,
				beatmapsetId = data.Beatmapset.Id,
				hash = data.Hash.HashValue,
				version = data.Version,
				mode = (int)data.Difficulty.Mode,
				star = data.Difficulty.Star,
				length = (long)data.Difficulty.Length.TotalMilliseconds,
				bpm = data.Difficulty.Bpm,
				cs = data.Difficulty.Cs,
				ar = data.Difficulty.Ar,
				od = data.Difficulty.Od,
				hp = data.Difficulty.Hp,
				objects = JsonSerializer.Serialize(data.Objects),
				locked = data.Locked ? 1 : 0,
				visible = data.Visible ? 1 : 0
			},
			transaction);

		await transaction.CommitAsync(cancellationToken);

		return new Beatmap { Id = id, Value = data };
	}

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"""
			INSERT INTO Beatmaps (Id, BeatmapsetId, Hash, Version, Mode, Star, Length, Bpm, Cs, Ar, Od, Hp, Objects, Locked, Visible)
			VALUES (@id, @beatmapsetId, @hash, @version, @mode, @star, @length, @bpm, @cs, @ar, @od, @hp, @objects, @locked, @visible)
			ON CONFLICT(Id) DO UPDATE SET
			    BeatmapsetId = excluded.BeatmapsetId,
			    Hash         = excluded.Hash,
			    Version      = excluded.Version,
			    Mode         = excluded.Mode,
			    Star         = excluded.Star,
			    Length       = excluded.Length,
			    Bpm          = excluded.Bpm,
			    Cs           = excluded.Cs,
			    Ar           = excluded.Ar,
			    Od           = excluded.Od,
			    Hp           = excluded.Hp,
			    Objects      = excluded.Objects,
			    Locked       = excluded.Locked,
			    Visible      = excluded.Visible
			""",
			new
			{
				id = beatmap.Id,
				beatmapsetId = beatmap.Value.Beatmapset.Id,
				hash = beatmap.Value.Hash.HashValue,
				version = beatmap.Value.Version,
				mode = (int)beatmap.Value.Difficulty.Mode,
				star = beatmap.Value.Difficulty.Star,
				length = (long)beatmap.Value.Difficulty.Length.TotalMilliseconds,
				bpm = beatmap.Value.Difficulty.Bpm,
				cs = beatmap.Value.Difficulty.Cs,
				ar = beatmap.Value.Difficulty.Ar,
				od = beatmap.Value.Difficulty.Od,
				hp = beatmap.Value.Difficulty.Hp,
				objects = JsonSerializer.Serialize(beatmap.Value.Objects),
				locked = beatmap.Value.Locked ? 1 : 0,
				visible = beatmap.Value.Visible ? 1 : 0
			});
	}

	/// <inheritdoc />
	public async ValueTask<Beatmap?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var row = await connection.QuerySingleOrDefaultAsync<BeatmapRow>(
			"""
			SELECT b.Id, b.BeatmapsetId, b.Hash, b.Version, b.Mode, b.Star, b.Length, b.Bpm, b.Cs, b.Ar, b.Od, b.Hp, b.Objects, b.Locked, b.Visible,
			       s.Id AS SetId, s.Artist AS SetArtist, s.Title AS SetTitle, s.Creator AS SetCreator, s.CreatedAt AS SetCreatedAt, s.UpdatedAt AS SetUpdatedAt, s.Locked AS SetLocked, s.Visible AS SetVisible
			FROM Beatmaps b
			JOIN Beatmapsets s ON s.Id = b.BeatmapsetId
			WHERE b.Id = @id
			""",
			new { id });
		return row is null ? null : Map(row);
	}

	/// <inheritdoc />
	public async ValueTask<Beatmap?> GetAsync(Md5 hash, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var row = await connection.QuerySingleOrDefaultAsync<BeatmapRow>(
			"""
			SELECT b.Id, b.BeatmapsetId, b.Hash, b.Version, b.Mode, b.Star, b.Length, b.Bpm, b.Cs, b.Ar, b.Od, b.Hp, b.Objects, b.Locked, b.Visible,
			       s.Id AS SetId, s.Artist AS SetArtist, s.Title AS SetTitle, s.Creator AS SetCreator, s.CreatedAt AS SetCreatedAt, s.UpdatedAt AS SetUpdatedAt, s.Locked AS SetLocked, s.Visible AS SetVisible
			FROM Beatmaps b
			JOIN Beatmapsets s ON s.Id = b.BeatmapsetId
			WHERE b.Hash = @hash
			""",
			new { hash = hash.HashValue });
		return row is null ? null : Map(row);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Beatmap>> ListAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var rows = await connection.QueryAsync<BeatmapRow>(
			"""
			SELECT b.Id, b.BeatmapsetId, b.Hash, b.Version, b.Mode, b.Star, b.Length, b.Bpm, b.Cs, b.Ar, b.Od, b.Hp, b.Objects, b.Locked, b.Visible,
			       s.Id AS SetId, s.Artist AS SetArtist, s.Title AS SetTitle, s.Creator AS SetCreator, s.CreatedAt AS SetCreatedAt, s.UpdatedAt AS SetUpdatedAt, s.Locked AS SetLocked, s.Visible AS SetVisible
			FROM Beatmaps b
			JOIN Beatmapsets s ON s.Id = b.BeatmapsetId
			WHERE b.BeatmapsetId = @setId
			ORDER BY b.Id
			""",
			new { setId = set.Id });
		return rows.Select(Map).ToList();
	}

	/// <inheritdoc />
	public async Task<Page<Beatmap>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var parameters = new DynamicParameters(new { limit = page.Limit, offset = page.Offset });
		var where = BeatmapQueryFilter.Build(query, parameters);
		var filter = where.Length == 0 ? "" : $" WHERE {where}";

		var sql = $"""
		           SELECT b.Id, b.BeatmapsetId, b.Hash, b.Version, b.Mode, b.Star, b.Length, b.Bpm, b.Cs, b.Ar, b.Od, b.Hp, b.Objects, b.Locked, b.Visible,
		                  s.Id AS SetId, s.Artist AS SetArtist, s.Title AS SetTitle, s.Creator AS SetCreator, s.CreatedAt AS SetCreatedAt, s.UpdatedAt AS SetUpdatedAt, s.Locked AS SetLocked, s.Visible AS SetVisible
		           FROM Beatmaps b
		           JOIN Beatmapsets s ON s.Id = b.BeatmapsetId{filter}
		           ORDER BY b.BeatmapsetId DESC, b.Star ASC
		           LIMIT @limit OFFSET @offset
		           """;

		var countSql = $"""
		                SELECT COUNT(*)
		                FROM Beatmaps b
		                JOIN Beatmapsets s ON s.Id = b.BeatmapsetId{filter}
		                """;

		var rows = await connection.QueryAsync<BeatmapRow>(sql, parameters);
		var total = await connection.ExecuteScalarAsync<int>(countSql, parameters);

		return new Page<Beatmap>(rows.Select(Map).ToList(), total);
	}

	/// <inheritdoc />
	public async Task RetainAsync(Beatmapset set, IReadOnlyCollection<Beatmap> keep,
		CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);

		if (keep.Count == 0)
		{
			await connection.ExecuteAsync(
				"DELETE FROM Beatmaps WHERE BeatmapsetId = @setId",
				new { setId = set.Id });
			return;
		}

		var keepIds = keep.Select(b => b.Id).ToArray();
		await connection.ExecuteAsync(
			"DELETE FROM Beatmaps WHERE BeatmapsetId = @setId AND Id NOT IN @keepIds",
			new { setId = set.Id, keepIds });
	}

	private static async Task<int> NextLocalIdAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction)
	{
		var max = await connection.ExecuteScalarAsync<int?>(
			"SELECT COALESCE(MAX(Id), 0) FROM Beatmaps", transaction: transaction);
		return Math.Max(Beatmap.LocalIdFloor, (max ?? 0) + 1);
	}

	private static Beatmap Map(BeatmapRow row)
	{
		var beatmapset = new Beatmapset
		{
			Id = (int)row.SetId,
			Value = new BeatmapsetData
			{
				Artist = row.SetArtist,
				Title = row.SetTitle,
				Creator = row.SetCreator,
				CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(row.SetCreatedAt),
				UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(row.SetUpdatedAt),
				Locked = row.SetLocked != 0,
				Visible = row.SetVisible != 0
			}
		};

		var objects = JsonSerializer.Deserialize<BeatmapObjects>(row.Objects)
		              ?? BeatmapObjects.NewFrom((GameMode)row.Mode);

		return new Beatmap
		{
			Id = (int)row.Id,
			Value = new BeatmapData
			{
				Hash = new Md5(row.Hash),
				Beatmapset = beatmapset,
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
				Locked = row.Locked != 0,
				Visible = row.Visible != 0
			}
		};
	}

	/// <summary>A stored row of the Beatmaps table joined with its beatmapset.</summary>
	private sealed class BeatmapRow
	{
		public long Id { get; set; }
		public long BeatmapsetId { get; set; }
		public string Hash { get; set; } = "";
		public string Version { get; set; } = "";
		public long Mode { get; set; }
		public double Star { get; set; }
		public long Length { get; set; }
		public double Bpm { get; set; }
		public double Cs { get; set; }
		public double Ar { get; set; }
		public double Od { get; set; }
		public double Hp { get; set; }
		public string Objects { get; set; } = "";
		public long Locked { get; set; }
		public long Visible { get; set; }
		public long SetId { get; set; }
		public string SetArtist { get; set; } = "";
		public string SetTitle { get; set; } = "";
		public string SetCreator { get; set; } = "";
		public long SetCreatedAt { get; set; }
		public long SetUpdatedAt { get; set; }
		public long SetLocked { get; set; }
		public long SetVisible { get; set; }
	}
}
