using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Scores;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Utilities;
using Basil.Infrastructure.Storage.Caching;
using Basil.Infrastructure.Storage.Multiplayer;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Scores;

/// <summary>Stores submitted scores.</summary>
internal sealed class SqliteScoreRepository(
	Database database,
	WriteBuffer buffer,
	IMatchRepository matches,
	IRoundRepository rounds,
	MatchReportCache reports) : CachedRepository<int, Score>(database, buffer), IScoreRepository
{
	protected override int KeyOf(Score item) => item.Id;

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
		await using var connection = await OpenAsync(cancellationToken);
		var id = await connection.QuerySingleOrDefaultAsync<int>(
			"""
			INSERT INTO Scores (UserId, BeatmapHash, Mode, Mods, Num300, Num100, Num50, NumGeki, NumKatu, NumMiss,
			                    TotalScore, MaxCombo, Grade, IsPassed, IsFullCombo, Timestamp, MatchId, RoundNumber, Team, Checksum)
			VALUES (@UserId, @BeatmapHash, @Mode, @Mods, @Num300, @Num100, @Num50, @NumGeki, @NumKatu, @NumMiss,
			        @TotalScore, @MaxCombo, @Grade, @IsPassed, @IsFullCombo, @Timestamp, @MatchId, @RoundNumber, @Team, @Checksum)
			ON CONFLICT(Checksum) DO NOTHING
			RETURNING Id
			""",
			Parameters(value));

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
		await using var connection = await OpenAsync(cancellationToken);
		var total = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM Scores {where}", parameters);
		parameters.Add("Limit", page.Limit);
		parameters.Add("Offset", page.Offset);
		var rows = await connection.QueryAsync<ScoreRow>(
			$"""
			 SELECT Id, UserId, BeatmapHash, Mode, Mods, Num300, Num100, Num50, NumGeki, NumKatu, NumMiss,
			        TotalScore, MaxCombo, Grade, IsPassed, IsFullCombo, Timestamp, MatchId, RoundNumber, Team, Checksum
			 FROM Scores
			 {where}
			 ORDER BY Timestamp DESC, Id DESC
			 LIMIT @Limit OFFSET @Offset
			""",
			parameters);

		var loaded = await LoadRoundsAsync(rows.Select(row => row.MatchId), cancellationToken);
		var items = rows.Select(row => Track(ToScore(row, FindRound(row, loaded)))).ToList();
		return new Page<Score>(items, total);
	}

	protected override async Task<Score?> ReadAsync(SqliteConnection connection, int key,
		CancellationToken cancellationToken)
	{
		var row = await connection.QuerySingleOrDefaultAsync<ScoreRow>(
			"""
			SELECT Id, UserId, BeatmapHash, Mode, Mods, Num300, Num100, Num50, NumGeki, NumKatu, NumMiss,
			       TotalScore, MaxCombo, Grade, IsPassed, IsFullCombo, Timestamp, MatchId, RoundNumber, Team, Checksum
			FROM Scores WHERE Id = @Id
			""",
			new { Id = key });
		if (row is null)
			return null;

		var loaded = await LoadRoundsAsync([row.MatchId], cancellationToken);
		return ToScore(row, FindRound(row, loaded));
	}

	protected override Task WriteAsync(SqliteConnection connection, SqliteTransaction transaction, Score score)
	{
		return connection.ExecuteAsync(
			"""
			INSERT INTO Scores (Id, UserId, BeatmapHash, Mode, Mods, Num300, Num100, Num50, NumGeki, NumKatu, NumMiss,
			                    TotalScore, MaxCombo, Grade, IsPassed, IsFullCombo, Timestamp, MatchId, RoundNumber, Team, Checksum)
			VALUES (@Id, @UserId, @BeatmapHash, @Mode, @Mods, @Num300, @Num100, @Num50, @NumGeki, @NumKatu, @NumMiss,
			        @TotalScore, @MaxCombo, @Grade, @IsPassed, @IsFullCombo, @Timestamp, @MatchId, @RoundNumber, @Team, @Checksum)
			ON CONFLICT(Id) DO UPDATE SET
				UserId = excluded.UserId,
				BeatmapHash = excluded.BeatmapHash,
				Mode = excluded.Mode,
				Mods = excluded.Mods,
				Num300 = excluded.Num300,
				Num100 = excluded.Num100,
				Num50 = excluded.Num50,
				NumGeki = excluded.NumGeki,
				NumKatu = excluded.NumKatu,
				NumMiss = excluded.NumMiss,
				TotalScore = excluded.TotalScore,
				MaxCombo = excluded.MaxCombo,
				Grade = excluded.Grade,
				IsPassed = excluded.IsPassed,
				IsFullCombo = excluded.IsFullCombo,
				Timestamp = excluded.Timestamp,
				MatchId = excluded.MatchId,
				RoundNumber = excluded.RoundNumber,
				Team = excluded.Team,
				Checksum = excluded.Checksum
			""",
			Parameters(score.Value, score.Id), transaction);
	}

	private static (string Where, DynamicParameters Parameters) BuildFilter(ScoreQuery query)
	{
		var conditions = new List<string>();
		var parameters = new DynamicParameters();
		if (query.Player is { } player)
		{
			parameters.Add("UserId", player.Id);
			conditions.Add("UserId = @UserId");
		}
		if (query.BeatmapHash is { } beatmapHash)
		{
			parameters.Add("BeatmapHash", beatmapHash.HashValue);
			conditions.Add("BeatmapHash = @BeatmapHash");
		}
		if (query.Match is { } match)
		{
			parameters.Add("MatchId", match.Id);
			conditions.Add("MatchId = @MatchId");
		}
		return (conditions.Count == 0 ? "" : $"WHERE {string.Join(" AND ", conditions)}", parameters);
	}

	private async Task<Dictionary<long, Dictionary<long, Round>>> LoadRoundsAsync(IEnumerable<long?> matchIds,
		CancellationToken cancellationToken)
	{
		var loaded = new Dictionary<long, Dictionary<long, Round>>();
		foreach (var matchId in matchIds.OfType<long>().Distinct())
		{
			var match = await matches.GetAsync((int)matchId, cancellationToken);
			if (match is null)
				continue;

			loaded[matchId] = (await rounds.ListAsync(match, cancellationToken))
				.ToDictionary(round => (long)round.Number);
		}
		return loaded;
	}

	private static Round? FindRound(ScoreRow row, Dictionary<long, Dictionary<long, Round>> loaded)
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
			row.UserId is { } userId ? (int)userId : null,
			row.BeatmapHash is { } beatmapHash ? new Md5(beatmapHash) : (Md5?)null,
			(GameMode)row.Mode,
			(GameMods)row.Mods,
			new HitCounts(row.Num300, row.Num100, row.Num50, row.NumGeki, row.NumKatu, row.NumMiss),
			row.TotalScore,
			(short)row.MaxCombo,
			(Grade)row.Grade,
			row.IsPassed != 0,
			row.IsFullCombo != 0,
			DateTimeOffset.FromUnixTimeMilliseconds(row.Timestamp))
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
			Mode = (long)value.Mode,
			Mods = (long)value.Mods,
			value.HitCounts.Num300,
			value.HitCounts.Num100,
			value.HitCounts.Num50,
			value.HitCounts.NumGeki,
			value.HitCounts.NumKatu,
			value.HitCounts.NumMiss,
			value.TotalScore,
			value.MaxCombo,
			Grade = (long)value.Grade,
			IsPassed = value.IsPassed ? 1 : 0,
			IsFullCombo = value.IsFullCombo ? 1 : 0,
			Timestamp = value.Timestamp.ToUnixTimeMilliseconds(),
			MatchId = value.Round?.Match.Id,
			RoundNumber = value.Round?.Number,
			Team = value.Team is { } team ? (long)team : (long?)null,
			Checksum = value.Checksum?.HashValue
		};
	}

	/// <summary>A stored row of the Scores table.</summary>
	private sealed class ScoreRow
	{
		public int Id { get; set; }
		public long? UserId { get; set; }
		public string? BeatmapHash { get; set; }
		public long Mode { get; set; }
		public long Mods { get; set; }
		public int Num300 { get; set; }
		public int Num100 { get; set; }
		public int Num50 { get; set; }
		public int NumGeki { get; set; }
		public int NumKatu { get; set; }
		public int NumMiss { get; set; }
		public int TotalScore { get; set; }
		public int MaxCombo { get; set; }
		public long Grade { get; set; }
		public long IsPassed { get; set; }
		public long IsFullCombo { get; set; }
		public long Timestamp { get; set; }
		public long? MatchId { get; set; }
		public long? RoundNumber { get; set; }
		public long? Team { get; set; }
		public string? Checksum { get; set; }
	}
}
