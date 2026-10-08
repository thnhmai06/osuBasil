using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Scores;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Utilities;
using Basil.Infrastructure.Storage.Memory;
using Basil.Infrastructure.Storage.Multiplayer;
using Basil.Infrastructure.Storage.Writing;
using Dapper;
using Npgsql;

namespace Basil.Infrastructure.Storage.Scores;

/// <summary>Stores submitted scores.</summary>
internal sealed class PostgresScoreRepository(
	Database database,
	DatabaseWriter writer,
	IMatchRepository matches,
	IRoundRepository rounds,
	MatchReportCache reports) : MemoryRepository<int, Score>(database, writer), IScoreRepository
{
	protected override int KeyOf(Score item) => item.Id;

	protected override Root RootOf(Score item) =>
		item.Value.Round is { } round ? Root.Match(round.Match.Id) : Root.User(item.Value.UserId ?? 0);

	/// <inheritdoc />
	/// <remarks>A duplicate checksum stores nothing and yields <see langword="null" />.</remarks>
	public async Task<Score?> CreateAsync(ScoreData data, CancellationToken cancellationToken = default)
	{
		var round = data.Round;
		if (round is not null)
		{
			var liveRounds = await rounds.ListAsync(round.Match, cancellationToken);
			round = liveRounds.FirstOrDefault(item => item.Number == round.Number) ?? round;
		}

		var value = data with { Round = round };
		var root = value.Round is { } scoreRound ? Root.Match(scoreRound.Match.Id) : Root.User(value.UserId ?? 0);
		var parameters = Parameters(value);
		int id;
		try
		{
			id = await Writer.WriteAsync(root, (connection, transaction) => connection.QuerySingleOrDefaultAsync<int>(
			"""
			insert into scores (user_id, beatmap_hash, mode, mods, num300, num100, num50, num_geki, num_katu, num_miss,
			                    total_score, max_combo, grade, is_passed, is_full_combo, timestamp, match_id, round_number, team, checksum)
			values (@UserId, @BeatmapHash, @Mode, @Mods, @Num300, @Num100, @Num50, @NumGeki, @NumKatu, @NumMiss,
			        @TotalScore, @MaxCombo, @Grade, @IsPassed, @IsFullCombo, @Timestamp, @MatchId, @RoundNumber, @Team, @Checksum)
			on conflict (checksum) do nothing
			returning id
			""",
			parameters, transaction), cancellationToken);
		}
		catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
		{
			return null;
		}

		if (id == 0)
			return null;

		var score = Track(new Score { Id = id, Value = value });
		if (round is not null)
			reports.Invalidate(round.Match.Id);
		return score;
	}

	/// <inheritdoc />
	public ValueTask<Score?> GetAsync(int id, CancellationToken cancellationToken = default) => FindAsync(id, cancellationToken);

	/// <inheritdoc />
	public async Task<Page<Score>> ListAsync(ScoreQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (where, parameters) = BuildFilter(query);
		parameters.Add("Limit", page.Limit);
		parameters.Add("Offset", page.Offset);
		var (total, rows) = await Database.ReadAsync(async connection => (
			await connection.ExecuteScalarAsync<int>($"select count(*) from scores {where}", parameters),
			await connection.QueryAsync<ScoreRow>(
			$"""
			 select id, user_id, beatmap_hash, mode, mods, num300, num100, num50, num_geki, num_katu, num_miss,
			        total_score, max_combo, grade, is_passed, is_full_combo, timestamp, match_id, round_number, team, checksum
			 from scores
			 {where}
			 order by timestamp desc, id desc
			 limit @Limit offset @Offset
			""",
			parameters)), cancellationToken);

		var loaded = await LoadRoundsAsync(rows.Select(row => row.MatchId), cancellationToken);
		var items = rows.Select(row => Track(ToScore(row, FindRound(row, loaded)))).ToList();
		return new Page<Score>(items, total);
	}

	protected override async Task<Score?> LoadAsync(int key)
	{
		var row = await Database.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<ScoreRow>(
			"""
			select id, user_id, beatmap_hash, mode, mods, num300, num100, num50, num_geki, num_katu, num_miss,
			       total_score, max_combo, grade, is_passed, is_full_combo, timestamp, match_id, round_number, team, checksum
			from scores where id = @Id
			""",
			new { Id = key }));
		if (row is null)
			return null;

		var loaded = await LoadRoundsAsync([row.MatchId], default);
		return ToScore(row, FindRound(row, loaded));
	}

	protected override string WriteSql =>
		"""
			insert into scores (id, user_id, beatmap_hash, mode, mods, num300, num100, num50, num_geki, num_katu, num_miss,
			                    total_score, max_combo, grade, is_passed, is_full_combo, timestamp, match_id, round_number, team, checksum)
			values (@Id, @UserId, @BeatmapHash, @Mode, @Mods, @Num300, @Num100, @Num50, @NumGeki, @NumKatu, @NumMiss,
			        @TotalScore, @MaxCombo, @Grade, @IsPassed, @IsFullCombo, @Timestamp, @MatchId, @RoundNumber, @Team, @Checksum)
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
				grade = excluded.grade,
				is_passed = excluded.is_passed,
				is_full_combo = excluded.is_full_combo,
				timestamp = excluded.timestamp,
				match_id = excluded.match_id,
				round_number = excluded.round_number,
				team = excluded.team,
				checksum = excluded.checksum;
			""";

	protected override object WriteParameters(Score score) => Parameters(score.Value, score.Id);

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
		if (query.Match is { } match)
		{
			parameters.Add("MatchId", match.Id);
			conditions.Add("match_id = @MatchId");
		}
		return (conditions.Count == 0 ? "" : $"where {string.Join(" AND ", conditions)}", parameters);
	}

	private async Task<Dictionary<int, Dictionary<int, Round>>> LoadRoundsAsync(IEnumerable<int?> matchIds,
		CancellationToken cancellationToken)
	{
		var loaded = new Dictionary<int, Dictionary<int, Round>>();
		foreach (var matchId in matchIds.OfType<int>().Distinct())
		{
			var match = await matches.GetAsync(matchId, cancellationToken);
			if (match is null)
				continue;

			loaded[matchId] = (await rounds.ListAsync(match, cancellationToken))
				.ToDictionary(round => round.Number);
		}
		return loaded;
	}

	private static Round? FindRound(ScoreRow row, Dictionary<int, Dictionary<int, Round>> loaded)
	{
		return row.MatchId is { } matchId && row.RoundNumber is { } roundNumber
		       && loaded.TryGetValue(matchId, out var roundsByNumber)
		       && roundsByNumber.TryGetValue(roundNumber, out var round)
			? round
			: null;
	}

	private static Score ToScore(ScoreRow row, Round? round)
	{
		return new Score { Id = row.Id, Value = ToData(row, round) };
	}

	private static ScoreData ToData(ScoreRow row, Round? round)
	{
		return new ScoreData(
			row.UserId,
			row.BeatmapHash is { } beatmapHash ? new Md5(beatmapHash) : (Md5?)null,
			(GameMode)row.Mode,
			(GameMods)row.Mods,
			new HitCounts(row.Num300, row.Num100, row.Num50, row.NumGeki, row.NumKatu, row.NumMiss),
			row.TotalScore,
			(short)row.MaxCombo,
			(Grade)row.Grade,
			row.IsPassed,
			row.IsFullCombo,
			row.Timestamp)
		with
		{
			Round = round,
			Checksum = row.Checksum is { } checksum ? new Md5(checksum) : (Md5?)null,
			Team = row.Team is { } team ? (GameTeam)team : null
		};
	}

	private static object Parameters(ScoreData value, int? id = null)
	{
		return new
		{
			Id = id,
			UserId = value.UserId,
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
			Grade = (int)value.Grade,
			IsPassed = value.IsPassed,
			IsFullCombo = value.IsFullCombo,
			Timestamp = value.Timestamp.ToUniversalTime(),
			MatchId = value.Round?.Match.Id,
			RoundNumber = value.Round?.Number,
			Team = value.Team is { } team ? (int)team : (int?)null,
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
		public int Grade { get; set; }
		public bool IsPassed { get; set; }
		public bool IsFullCombo { get; set; }
		public DateTimeOffset Timestamp { get; set; }
		public int? MatchId { get; set; }
		public int? RoundNumber { get; set; }
		public int? Team { get; set; }
		public string? Checksum { get; set; }
	}
}
