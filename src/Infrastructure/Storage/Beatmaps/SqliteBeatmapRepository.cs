using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Utilities;
using Basil.Infrastructure.Storage.Caching;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Beatmaps;

/// <summary>Stores beatmap difficulties in the <c>Beatmaps</c> table.</summary>
internal sealed class SqliteBeatmapRepository : CachedRepository<int, Beatmap>, IBeatmapRepository
{
	private readonly WriteBuffer _buffer;
	private readonly IBeatmapsetRepository _beatmapsets;
	private readonly ConcurrentDictionary<Md5, int> _idsByHash = new();
	private readonly ConcurrentDictionary<int, Md5> _hashesById = new();
	private readonly IdentityMap<int, ImmutableList<Beatmap>> _bySet = new();

	public SqliteBeatmapRepository(Database database, WriteBuffer buffer, IBeatmapsetRepository beatmapsets)
		: base(database, buffer)
	{
		_buffer = buffer;
		_beatmapsets = beatmapsets;
	}

	protected override int KeyOf(Beatmap item) => item.Id;

	/// <inheritdoc />
	public async Task<Beatmap> CreateAsync(BeatmapData data, int? onlineId = null,
		CancellationToken cancellationToken = default)
	{
		var set = await LiveSetAsync(data.Beatmapset, cancellationToken);
		var value = WithSet(data, set);
		await using var connection = await OpenAsync(cancellationToken);
		await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

		var id = onlineId ?? await NextLocalIdAsync(connection, transaction);
		await connection.ExecuteAsync(
			"""
			INSERT INTO Beatmaps (Id, BeatmapsetId, Hash, Version, Mode, Star, Length, Bpm, Cs, Ar, Od, Hp, Objects, Locked, Visible)
			VALUES (@Id, @BeatmapsetId, @Hash, @Version, @Mode, @Star, @Length, @Bpm, @Cs, @Ar, @Od, @Hp, @Objects, @Locked, @Visible)
			""",
			Parameters(id, value), transaction);

		await transaction.CommitAsync(cancellationToken);
		var beatmap = Track(new Beatmap { Id = id, Value = value });
		Index(beatmap);
		_bySet.TryUpdate(set.Id, current => AddOrReplace(current, beatmap));
		return beatmap;
	}

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
	{
		var value = WithSet(beatmap.Value, await LiveSetAsync(beatmap.Value.Beatmapset, cancellationToken));
		if (!_hashesById.TryGetValue(beatmap.Id, out var stored) || stored != value.Hash)
		{
			// A hash must be unique across every beatmap, so a new or changed one is written at once and the live
			// beatmap takes it only after the commit.
			await using var connection = await OpenAsync(cancellationToken);
			await using var transaction = connection.BeginTransaction();
			try
			{
				await WriteAsync(connection, transaction, new Beatmap { Id = beatmap.Id, Value = value });
				await transaction.CommitAsync(cancellationToken);
			}
			catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
			{
				throw new InvalidOperationException($"Another beatmap already has the hash {value.Hash}.", exception);
			}

			Remember(beatmap, value);
			return;
		}

		Save(Remember(beatmap, value));
	}

	/// <summary>Gives the live instance of a beatmap the stored data and keeps the lookups in step.</summary>
	private Beatmap Remember(Beatmap beatmap, BeatmapData value)
	{
		var live = Track(beatmap);
		live.Value = value;
		Index(live);
		_bySet.TryUpdate(value.Beatmapset.Id, current => AddOrReplace(current, live));
		return live;
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
		if (_idsByHash.TryGetValue(hash, out var id))
		{
			var cached = await GetAsync(id, cancellationToken);
			if (cached is not null && cached.Value.Hash == hash)
				return cached;

			_idsByHash.TryRemove(new KeyValuePair<Md5, int>(hash, id));
		}

		await using var connection = await OpenAsync(cancellationToken);
		var row = await connection.QuerySingleOrDefaultAsync<BeatmapRow>(ByHashSql, new { Hash = hash.HashValue });
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
		var items = await _bySet.GetOrAddAsync(liveSet.Id, async _ =>
		{
			await using var connection = await OpenAsync(cancellationToken);
			var rows = await connection.QueryAsync<BeatmapRow>(BySetSql, new { SetId = liveSet.Id });
			var beatmaps = ImmutableList.CreateBuilder<Beatmap>();
			foreach (var row in rows)
			{
				var beatmap = Track(await ToBeatmapAsync(row, cancellationToken));
				Index(beatmap);
				beatmaps.Add(beatmap);
			}

			return beatmaps.ToImmutable();
		});

		return items ?? ImmutableList<Beatmap>.Empty;
	}

	/// <inheritdoc />
	public async Task<Page<Beatmap>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		await using var connection = await OpenAsync(cancellationToken);
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
	public Task RetainAsync(Beatmapset set, IReadOnlyCollection<Beatmap> keep,
		CancellationToken cancellationToken = default)
	{
		var keepIds = keep.Select(beatmap => beatmap.Id).ToArray();
		var keepSet = keepIds.ToHashSet();
		foreach (var beatmap in Items.Values.Where(beatmap => beatmap.Value.Beatmapset.Id == set.Id && !keepSet.Contains(beatmap.Id)))
			RemoveCached(beatmap);

		_bySet.TryUpdate(set.Id, current => current.Where(beatmap => keepSet.Contains(beatmap.Id)).ToImmutableList());
		_buffer.Enqueue((GetType(), "retain", set.Id), async (connection, transaction) =>
		{
			if (keepIds.Length == 0)
			{
				await connection.ExecuteAsync("DELETE FROM Beatmaps WHERE BeatmapsetId = @SetId", new { SetId = set.Id }, transaction);
				return;
			}

			await connection.ExecuteAsync(
				"DELETE FROM Beatmaps WHERE BeatmapsetId = @SetId AND Id NOT IN @KeepIds",
				new { SetId = set.Id, KeepIds = keepIds }, transaction);
		});
		return Task.CompletedTask;
	}

	protected override async Task<Beatmap?> ReadAsync(SqliteConnection connection, int key,
		CancellationToken cancellationToken)
	{
		var row = await connection.QuerySingleOrDefaultAsync<BeatmapRow>(ByIdSql, new { Id = key });
		return row is null ? null : await ToBeatmapAsync(row, cancellationToken);
	}

	protected override Task WriteAsync(SqliteConnection connection, SqliteTransaction transaction, Beatmap beatmap)
	{
		var value = beatmap.Value;
		return connection.ExecuteAsync(
			"""
			INSERT INTO Beatmaps (Id, BeatmapsetId, Hash, Version, Mode, Star, Length, Bpm, Cs, Ar, Od, Hp, Objects, Locked, Visible)
			VALUES (@Id, @BeatmapsetId, @Hash, @Version, @Mode, @Star, @Length, @Bpm, @Cs, @Ar, @Od, @Hp, @Objects, @Locked, @Visible)
			ON CONFLICT(Id) DO UPDATE SET
				BeatmapsetId = excluded.BeatmapsetId,
				Hash = excluded.Hash,
				Version = excluded.Version,
				Mode = excluded.Mode,
				Star = excluded.Star,
				Length = excluded.Length,
				Bpm = excluded.Bpm,
				Cs = excluded.Cs,
				Ar = excluded.Ar,
				Od = excluded.Od,
				Hp = excluded.Hp,
				Objects = excluded.Objects,
				Locked = excluded.Locked,
				Visible = excluded.Visible;
			""",
			Parameters(beatmap.Id, value), transaction);
	}

	protected override Task EraseAsync(SqliteConnection connection, SqliteTransaction transaction, int key)
	{
		return connection.ExecuteAsync("DELETE FROM Beatmaps WHERE Id = @Id", new { Id = key }, transaction);
	}

	private const string ByIdSql = """
		SELECT b.Id, b.BeatmapsetId, b.Hash, b.Version, b.Mode, b.Star, b.Length, b.Bpm, b.Cs, b.Ar, b.Od, b.Hp, b.Objects, b.Locked, b.Visible,
		       s.Id AS SetId, s.Artist AS SetArtist, s.Title AS SetTitle, s.Creator AS SetCreator, s.CreatedAt AS SetCreatedAt, s.UpdatedAt AS SetUpdatedAt, s.Locked AS SetLocked, s.Visible AS SetVisible
		FROM Beatmaps b
		JOIN Beatmapsets s ON s.Id = b.BeatmapsetId
		WHERE b.Id = @Id
		""";

	private const string ByHashSql = """
		SELECT b.Id, b.BeatmapsetId, b.Hash, b.Version, b.Mode, b.Star, b.Length, b.Bpm, b.Cs, b.Ar, b.Od, b.Hp, b.Objects, b.Locked, b.Visible,
		       s.Id AS SetId, s.Artist AS SetArtist, s.Title AS SetTitle, s.Creator AS SetCreator, s.CreatedAt AS SetCreatedAt, s.UpdatedAt AS SetUpdatedAt, s.Locked AS SetLocked, s.Visible AS SetVisible
		FROM Beatmaps b
		JOIN Beatmapsets s ON s.Id = b.BeatmapsetId
		WHERE b.Hash = @Hash
		""";

	private const string BySetSql = """
		SELECT b.Id, b.BeatmapsetId, b.Hash, b.Version, b.Mode, b.Star, b.Length, b.Bpm, b.Cs, b.Ar, b.Od, b.Hp, b.Objects, b.Locked, b.Visible,
		       s.Id AS SetId, s.Artist AS SetArtist, s.Title AS SetTitle, s.Creator AS SetCreator, s.CreatedAt AS SetCreatedAt, s.UpdatedAt AS SetUpdatedAt, s.Locked AS SetLocked, s.Visible AS SetVisible
		FROM Beatmaps b
		JOIN Beatmapsets s ON s.Id = b.BeatmapsetId
		WHERE b.BeatmapsetId = @SetId
		ORDER BY b.Id
		""";

	private void RemoveCached(Beatmap beatmap)
	{
		Items.Remove(beatmap.Id);
		Unindex(beatmap);
	}

	private void Unindex(Beatmap beatmap)
	{
		if (_hashesById.TryRemove(beatmap.Id, out var hash))
			_idsByHash.TryRemove(new KeyValuePair<Md5, int>(hash, beatmap.Id));
	}

	private void Index(Beatmap beatmap)
	{
		var hash = beatmap.Value.Hash;
		if (_hashesById.TryGetValue(beatmap.Id, out var previous) && previous != hash)
			_idsByHash.TryRemove(new KeyValuePair<Md5, int>(previous, beatmap.Id));

		_hashesById[beatmap.Id] = hash;
		_idsByHash[hash] = beatmap.Id;
	}

	private static ImmutableList<Beatmap> AddOrReplace(ImmutableList<Beatmap> current, Beatmap beatmap)
	{
		var index = current.FindIndex(item => item.Id == beatmap.Id);
		return index < 0 ? current.Add(beatmap) : current.SetItem(index, beatmap);
	}

	private static async Task<int> NextLocalIdAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction)
	{
		var max = await connection.ExecuteScalarAsync<int?>("SELECT COALESCE(MAX(Id), 0) FROM Beatmaps", transaction: transaction);
		return Math.Max(Beatmap.LocalIdFloor, (max ?? 0) + 1);
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
			Length = (long)value.Difficulty.Length.TotalMilliseconds,
			value.Difficulty.Bpm,
			Cs = value.Difficulty.Cs,
			Ar = value.Difficulty.Ar,
			Od = value.Difficulty.Od,
			Hp = value.Difficulty.Hp,
			Objects = JsonSerializer.Serialize(value.Objects),
			Locked = value.Locked ? 1 : 0,
			Visible = value.Visible ? 1 : 0
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
		}, cancellationToken);
		var objects = JsonSerializer.Deserialize<BeatmapObjects>(row.Objects)
		              ?? BeatmapObjects.NewFrom((GameMode)row.Mode);

		return new Beatmap
		{
			Id = (int)row.Id,
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
				Locked = row.Locked != 0,
				Visible = row.Visible != 0
			}
		};
	}

	/// <summary>A stored beatmap row joined with its beatmapset.</summary>
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
