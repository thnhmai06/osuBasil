using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Scores;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Writing;
using Basil.Infrastructure.Storage.Memory;
using Dapper;
using Npgsql;

namespace Basil.Infrastructure.Storage.Scores;

/// <summary>Stores submitted scores.</summary>
internal sealed class PostgresScoreRepository(
	DatabaseReader reader,
	DatabaseWriter writer) : MemoryRepository<int, ScoreRecord>(reader, writer), IScoreRepository
{
	private readonly WeakIndex<Md5, ScoreRecord> _byChecksum = new();

	protected override string WriteSql =>
		"""
		insert into scores (id, user_id, beatmap_hash, mode, mods, num300, num100, num50, num_geki, num_katu, num_miss,
		                    total_score, max_combo, current_combo, grade, is_passed, is_full_combo, timestamp, checksum)
		values (@Id, @UserId, @BeatmapHash, @Mode, @Mods, @Num300, @Num100, @Num50, @NumGeki, @NumKatu, @NumMiss,
		        @TotalScore, @MaxCombo, @CurrentCombo, @Grade, @IsPassed, @IsFullCombo, @Timestamp, @Checksum)
		on conflict (id) do update set
			user_id = excluded.user_id,
			beatmap_hash = excluded.beatmap_hash,
			mode = excluded.mode,
			mods = excluded.mods,
			num300 = excluded.num300,
			num100 = excluded.num100,
			num50 = excluded.num50,
			num_geki = excluded.num_geki,
			num_katu = excluded.num_katu,
			num_miss = excluded.num_miss,
			total_score = excluded.total_score,
			max_combo = excluded.max_combo,
			current_combo = excluded.current_combo,
			grade = excluded.grade,
			is_passed = excluded.is_passed,
			is_full_combo = excluded.is_full_combo,
			timestamp = excluded.timestamp,
			checksum = excluded.checksum;
		""";

	/// <inheritdoc />
	public async Task<ScoreRecord> CreateAsync(ScoreData data, CancellationToken cancellationToken = default)
	{
		if (data.Checksum is { } checksum && _byChecksum.TryGetValue(checksum, out var known) &&
		    known.Value.Checksum == checksum)
			throw new AlreadyExistsException($"A score with checksum {checksum} is already stored.");

		var parameters = Parameters(data);
		int id;
		try
		{
			id = await Writer.WriteAsync(Root.User(data.UserId ?? 0), (connection, transaction) =>
				connection.QuerySingleOrDefaultAsync<int>(
					"""
					insert into scores (user_id, beatmap_hash, mode, mods, num300, num100, num50, num_geki, num_katu, num_miss,
					                    total_score, max_combo, current_combo, grade, is_passed, is_full_combo, timestamp, checksum)
					values (@UserId, @BeatmapHash, @Mode, @Mods, @Num300, @Num100, @Num50, @NumGeki, @NumKatu, @NumMiss,
					        @TotalScore, @MaxCombo, @CurrentCombo, @Grade, @IsPassed, @IsFullCombo, @Timestamp, @Checksum)
					on conflict (checksum) do nothing
					returning id
					""",
					parameters, transaction), cancellationToken);
		}
		catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
		{
			throw new AlreadyExistsException("A score with the same checksum is already stored.");
		}

		if (id == 0)
			throw new AlreadyExistsException("A score with the same checksum is already stored.");

		var score = Track(new ScoreRecord { Id = id, Value = data });
		IndexScore(score);
		return score;
	}

	/// <inheritdoc />
	public async ValueTask<ScoreRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		var score = await FindAsync(id, cancellationToken);
		if (score is not null)
			IndexScore(score);
		return score;
	}

	/// <inheritdoc />
	public async Task<Page<ScoreRecord>> ListAsync(ScoreQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (where, parameters) = BuildFilter(query);
		var source = query.BestPerPlayer
			? $"(select distinct on (user_id) * from scores {where} order by user_id, total_score desc, timestamp asc, id asc) best_scores"
			: $"scores {where}";
		var order = query.BestPerPlayer || query.BeatmapHash is not null
			? "total_score desc, timestamp asc, id asc"
			: "timestamp desc, id desc";
		parameters.Add("Limit", page.Limit);
		parameters.Add("Offset", page.Offset);
		var (total, rows) = await Reader.ReadAsync(async connection => (
			await connection.ExecuteScalarAsync<int>($"select count(*) from {source}", parameters),
			await connection.QueryAsync<ScoreRow>(
				$"""
				  select id, user_id, beatmap_hash, mode, mods, num300, num100, num50, num_geki, num_katu, num_miss,
				         total_score, max_combo, current_combo, grade, is_passed, is_full_combo, timestamp, checksum
				  from {source}
				  order by {order}
				  limit @Limit offset @Offset
				 """,
				parameters)), cancellationToken);

		var items = rows.Select(row => Track(ToScore(row))).ToList();
		foreach (var score in items)
			IndexScore(score);
		return new Page<ScoreRecord>(items, total);
	}

	protected override int KeyOf(ScoreRecord item)
	{
		return item.Id;
	}

	protected override Root RootOf(ScoreRecord item)
	{
		return Root.User(item.Value.UserId ?? 0);
	}

	protected override async Task<ScoreRecord?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<ScoreRow>(
			"""
			select id, user_id, beatmap_hash, mode, mods, num300, num100, num50, num_geki, num_katu, num_miss,
			       total_score, max_combo, current_combo, grade, is_passed, is_full_combo, timestamp, checksum
			from scores where id = @Id
			""",
			new { Id = key }));
		return row is null ? null : ToScore(row);
	}

	protected override object WriteParameters(ScoreRecord score)
	{
		return Parameters(score.Value, score.Id);
	}

	private static (string Where, DynamicParameters Parameters) BuildFilter(ScoreQuery query)
	{
		var conditions = new List<string>();
		var parameters = new DynamicParameters();
		if (query.Player is { } player)
		{
			parameters.Add("UserId", player.Id);
			conditions.Add("user_id = @UserId");
		}

		if (query.BeatmapHash is { } beatmapHash)
		{
			parameters.Add("BeatmapHash", beatmapHash.HashValue);
			conditions.Add("beatmap_hash = @BeatmapHash");
		}

		if (query.Mods is { } mods)
		{
			parameters.Add("Mods", (int)mods);
			conditions.Add("mods = @Mods");
		}

		if (query.Players is { } players)
		{
			parameters.Add("PlayerIds", players.Select(player => player.Id).ToArray());
			conditions.Add("user_id = any(@PlayerIds)");
		}

		if (query.BestPerPlayer)
			conditions.Add("user_id is not null");

		return (conditions.Count == 0 ? "" : $"where {string.Join(" AND ", conditions)}", parameters);
	}

	private void IndexScore(ScoreRecord score)
	{
		if (score.Value.Checksum is { } checksum)
			_byChecksum.Set(checksum, score);
	}

	private static ScoreRecord ToScore(ScoreRow row)
	{
		return new ScoreRecord { Id = row.Id, Value = ToData(row) };
	}

	private static ScoreData ToData(ScoreRow row)
	{
		return new ScoreData(
				row.UserId,
				row.BeatmapHash is { } beatmapHash ? new Md5(beatmapHash) : (Md5?)null,
				(GameMode)row.Mode,
				(GameMods)row.Mods,
				new HitCounts(row.Num300, row.Num100, row.Num50, row.NumGeki, row.NumKatu, row.NumMiss),
				row.TotalScore,
				(short)row.MaxCombo,
				(short)row.CurrentCombo,
				(Grade)row.Grade,
				row.IsPassed,
				row.IsFullCombo,
				row.Timestamp)
			with
			{
				Checksum = row.Checksum is { } checksum ? new Md5(checksum) : (Md5?)null
			};
	}

	private static object Parameters(ScoreData value, int? id = null)
	{
		return new
		{
			Id = id,
			value.UserId,
			BeatmapHash = value.BeatmapHash?.HashValue,
			Mode = (int)value.Mode,
			Mods = (int)value.Mods,
			value.HitCounts.Num300,
			value.HitCounts.Num100,
			value.HitCounts.Num50,
			value.HitCounts.NumGeki,
			value.HitCounts.NumKatu,
			value.HitCounts.NumMiss,
			value.TotalScore,
			value.MaxCombo,
			value.CurrentCombo,
			Grade = (int)value.Grade,
			value.IsPassed,
			value.IsFullCombo,
			Timestamp = value.Timestamp.ToUniversalTime(),
			Checksum = value.Checksum?.HashValue
		};
	}

	/// <summary>A stored row of the <c>scores</c> table.</summary>
	private sealed class ScoreRow
	{
		public int Id { get; set; }
		public int? UserId { get; set; }
		public string? BeatmapHash { get; set; }
		public int Mode { get; set; }
		public int Mods { get; set; }
		public int Num300 { get; set; }
		public int Num100 { get; set; }
		public int Num50 { get; set; }
		public int NumGeki { get; set; }
		public int NumKatu { get; set; }
		public int NumMiss { get; set; }
		public int TotalScore { get; set; }
		public int MaxCombo { get; set; }
		public int CurrentCombo { get; set; }
		public int Grade { get; set; }
		public bool IsPassed { get; set; }
		public bool IsFullCombo { get; set; }
		public DateTimeOffset Timestamp { get; set; }
		public string? Checksum { get; set; }
	}
}