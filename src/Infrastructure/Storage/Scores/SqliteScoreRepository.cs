using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Scores;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Utilities;
using Dapper;

namespace Basil.Infrastructure.Storage.Scores;

/// <summary>Stores submitted scores.</summary>
internal sealed class SqliteScoreRepository(
	Database database,
	IMatchRepository matches,
	IRoundRepository rounds) : IScoreRepository
{
	/// <inheritdoc />
	/// <remarks>A duplicate checksum stores nothing and yields <see langword="null" />.</remarks>
	public async Task<Score?> CreateAsync(ScoreData data, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var id = await connection.QuerySingleOrDefaultAsync<int>(
			"""
			INSERT INTO Scores (UserId, BeatmapHash, Mode, Mods, Num300, Num100, Num50, NumGeki, NumKatu, NumMiss,
			                    TotalScore, MaxCombo, Grade, IsPassed, IsFullCombo, Timestamp, MatchId, RoundNumber, Team, Checksum)
			VALUES (@UserId, @BeatmapHash, @Mode, @Mods, @Num300, @Num100, @Num50, @NumGeki, @NumKatu, @NumMiss,
			        @TotalScore, @MaxCombo, @Grade, @IsPassed, @IsFullCombo, @Timestamp, @MatchId, @RoundNumber, @Team, @Checksum)
			ON CONFLICT(Checksum) DO NOTHING
			RETURNING Id
			""",
			new
			{
				UserId = data.UserId,
				BeatmapHash = data.BeatmapHash?.HashValue,
				Mode = (long)data.Mode,
				Mods = (long)data.Mods,
				data.HitCounts.Num300,
				data.HitCounts.Num100,
				data.HitCounts.Num50,
				data.HitCounts.NumGeki,
				data.HitCounts.NumKatu,
				data.HitCounts.NumMiss,
				data.TotalScore,
				data.MaxCombo,
				Grade = (long)data.Grade,
				IsPassed = data.IsPassed ? 1 : 0,
				IsFullCombo = data.IsFullCombo ? 1 : 0,
				Timestamp = data.Timestamp.ToUnixTimeMilliseconds(),
				MatchId = data.Round?.Match.Id,
				RoundNumber = data.Round?.Number,
				Team = data.Team is { } team ? (long)team : (long?)null,
				Checksum = data.Checksum?.HashValue
			});

		return id == 0 ? null : new Score { Id = id, Value = data };
	}

	/// <inheritdoc />
	public async ValueTask<Score?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var row = await connection.QuerySingleOrDefaultAsync<ScoreRow>(
			"""
			SELECT Id, UserId, BeatmapHash, Mode, Mods, Num300, Num100, Num50, NumGeki, NumKatu, NumMiss,
			       TotalScore, MaxCombo, Grade, IsPassed, IsFullCombo, Timestamp, MatchId, RoundNumber, Team, Checksum
			FROM Scores
			WHERE Id = @Id
			""",
			new { Id = id });
		if (row is null) return null;

		var loaded = await LoadRoundsAsync([row.MatchId], cancellationToken);
		return new Score { Id = row.Id, Value = ToData(row, FindRound(row, loaded)) };
	}

	/// <inheritdoc />
	public async Task<Page<Score>> ListAsync(ScoreQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
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

		var where = conditions.Count == 0 ? "" : $"WHERE {string.Join(" AND ", conditions)}";

		await using var connection = await database.OpenAsync(cancellationToken);
		var total = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM Scores {where}", parameters);

		parameters.Add("Limit", page.Limit);
		parameters.Add("Offset", page.Offset);
		var rows = (await connection.QueryAsync<ScoreRow>(
			$"""
			 SELECT Id, UserId, BeatmapHash, Mode, Mods, Num300, Num100, Num50, NumGeki, NumKatu, NumMiss,
			        TotalScore, MaxCombo, Grade, IsPassed, IsFullCombo, Timestamp, MatchId, RoundNumber, Team, Checksum
			 FROM Scores
			 {where}
			 ORDER BY Timestamp DESC, Id DESC
			 LIMIT @Limit OFFSET @Offset
			 """,
			parameters)).ToList();

		var loaded = await LoadRoundsAsync(rows.Select(row => row.MatchId), cancellationToken);

		var scores = new List<Score>(rows.Count);
		foreach (var row in rows)
			scores.Add(new Score { Id = row.Id, Value = ToData(row, FindRound(row, loaded)) });

		return new Page<Score>(scores, total);
	}

	/// <summary>Loads the rounds of the given matches once each, keyed by match id and then round number.</summary>
	private async Task<Dictionary<long, Dictionary<long, Round>>> LoadRoundsAsync(IEnumerable<long?> matchIds,
		CancellationToken cancellationToken)
	{
		var loaded = new Dictionary<long, Dictionary<long, Round>>();
		foreach (var matchId in matchIds.OfType<long>().Distinct())
		{
			var match = await matches.GetAsync((int)matchId, cancellationToken);
			if (match is null) continue;

			loaded[matchId] = (await rounds.ListAsync(match, cancellationToken))
				.ToDictionary(round => (long)round.Number);
		}

		return loaded;
	}

	/// <summary>Finds the round a stored score was played in, or <see langword="null" /> when it names none.</summary>
	private static Round? FindRound(ScoreRow row, Dictionary<long, Dictionary<long, Round>> loaded)
	{
		return row.MatchId is { } matchId && row.RoundNumber is { } roundNumber
		       && loaded.TryGetValue(matchId, out var roundsByNumber)
		       && roundsByNumber.TryGetValue(roundNumber, out var round)
			? round
			: null;
	}

	/// <summary>Builds the data of a score from its stored row and its round, if any.</summary>
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
