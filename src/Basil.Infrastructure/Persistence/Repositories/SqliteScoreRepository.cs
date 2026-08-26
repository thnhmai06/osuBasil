using Basil.Application.Abstractions.Scores;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IScoreRepository" />
/// <remarks>
///     Rows map through the private mutable <c>*RowDto</c> DTOs; the stored integer enum columns
///     are cast to their domain enum types during mapping. Each method opens its own connection.
/// </remarks>
public sealed class SqliteScoreRepository(string connectionString, ILogger<SqliteScoreRepository> logger)
	: IScoreRepository
{
	/// <inheritdoc />
	public async Task<ScoreOwner?> FetchOwnerAsync(long scoreId, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var row = await connection.QuerySingleOrDefaultAsync<ScoreOwnerRowDto>(
			"SELECT UserId AS UserId, Mode AS Mode FROM Scores WHERE Id = @ScoreId",
			new { ScoreId = scoreId });
		return row?.ToRow();
	}

	/// <inheritdoc />
	/// <remarks>
	///     The insert and the id read-back are one batched statement, so the returned id is the
	///     immediately inserted row's.
	/// </remarks>
	public async Task<long> CreateAsync(ScoreInsertRow row, CancellationToken cancellationToken = default)
	{
		return await SqliteInstrumentation.RecordAsync("score.create", async () =>
		{
			await using var connection = Connect();
			var id = await connection.QuerySingleAsync<long>(
				"""
				INSERT INTO Scores
				    (MapMd5, Score, Accuracy, MaxCombo, Mods, N300, N100, N50, NMiss, NGeki, NKatu,
				     Grade, Mode, PlayTime, TimeElapsed, ClientFlags, UserId, Perfect, Checksum, RoundId, Team, SubmittedAt)
				VALUES
				    (@MapMd5, @Score, @Accuracy, @MaxCombo, @Mods, @N300, @N100, @N50, @NMiss, @NGeki, @NKatu,
				     @Grade, @Mode, @PlayTime, @TimeElapsed, @ClientFlags, @UserId, @Perfect, @Checksum, @RoundId, @Team, @SubmittedAt);
				SELECT last_insert_rowid();
				""",
				row);
			logger.LogDebug("Score row created: Id={Id} UserId={UserId}", id, row.UserId);
			return id;
		});
	}

	/// <inheritdoc />
	public async Task<bool> CheckExistAsync(string checksum,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		return await connection.ExecuteScalarAsync<bool>(
			"SELECT EXISTS(SELECT 1 FROM Scores WHERE Checksum = @Checksum)",
			new { Checksum = checksum });
	}

	/// <inheritdoc />
	public async Task<ScoreRow?> FetchByIdAsync(long id, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var row = await connection.QuerySingleOrDefaultAsync<ScoreRowDto>(
			"""
			SELECT Id, RoundId, Team, MapMd5, Score, Accuracy, MaxCombo, Mods, N300, N100, N50, NMiss,
			       NGeki, NKatu, Grade, Mode, PlayTime, TimeElapsed, ClientFlags, UserId, Perfect,
			       Checksum, SubmittedAt
			FROM Scores
			WHERE Id = @Id
			""",
			new { Id = id });
		return row?.ToRow();
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<ScoreRow>> FetchPageAsync(int offset, int limit,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var rows = await connection.QueryAsync<ScoreRowDto>(
			"""
			SELECT Id, RoundId, Team, MapMd5, Score, Accuracy, MaxCombo, Mods, N300, N100, N50, NMiss,
			       NGeki, NKatu, Grade, Mode, PlayTime, TimeElapsed, ClientFlags, UserId, Perfect,
			       Checksum, SubmittedAt
			FROM Scores
			ORDER BY Id DESC
			LIMIT @Limit OFFSET @Offset
			""",
			new { Limit = limit, Offset = offset });
		return [.. rows.Select(r => r.ToRow())];
	}

	/// <inheritdoc />
	/// <remarks>
	///     Joins the Users table so each score carries its submitter's name and orders the round's
	///     scores by score descending.
	/// </remarks>
	public async Task<IReadOnlyList<ScoreReport>> FetchByRoundAsync(int roundId,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var rows = await connection.QueryAsync<RoundScoreRowDto>(
			"""
			SELECT s.Id, s.UserId, u.Name AS UserName, s.Team, s.Mods, s.Score, s.Accuracy, s.MaxCombo,
			       s.N300, s.N100, s.N50, s.NMiss, s.NGeki, s.NKatu, s.Grade, s.Perfect, s.SubmittedAt
			FROM Scores s
			JOIN Users u ON u.Id = s.UserId
			WHERE s.RoundId = @RoundId
			ORDER BY s.Score DESC
			""",
			new { RoundId = roundId });
		return [.. rows.Select(r => r.ToRow())];
	}

	/// <inheritdoc />
	/// <remarks>
	///     The count is read from the Counters table under the <c>Scores:Total</c> counter, not
	///     counted from the Scores table directly.
	/// </remarks>
	public async Task<int> FetchCountAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		return await connection.ExecuteScalarAsync<int>(
			"SELECT Value FROM Counters WHERE Name = 'Scores:Total'");
	}

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return SqliteConnectionFactory.Open(connectionString);
	}

	/// <summary>
	///     A mutable row DTO matching the round-score SELECT's columns.
	/// </summary>
	private sealed class RoundScoreRowDto
	{
		public long Id { get; set; }
		public int UserId { get; set; }
		public string UserName { get; set; } = "";
		public int? Team { get; set; }
		public int Mods { get; set; }
		public long Score { get; set; }
		public double Accuracy { get; set; }
		public int MaxCombo { get; set; }
		public int N300 { get; set; }
		public int N100 { get; set; }
		public int N50 { get; set; }
		public int NMiss { get; set; }
		public int NGeki { get; set; }
		public int NKatu { get; set; }
		public string Grade { get; set; } = "N";
		public bool Perfect { get; set; }
		public DateTime SubmittedAt { get; set; }

		/// <summary>
		///     Builds a <see cref="ScoreReport" /> from this row, casting the stored enum columns.
		/// </summary>
		/// <returns>The domain round score row.</returns>
		public ScoreReport ToRow()
		{
			return new ScoreReport(
				Id, UserId, UserName, (MatchTeam?)Team, (Mods)Mods, Score, Accuracy, MaxCombo, N300, N100, N50,
				NMiss, NGeki, NKatu, Grade, Perfect, SubmittedAt);
		}
	}

	/// <summary>
	///     A mutable row DTO matching the owner lookup SELECT's columns.
	/// </summary>
	private sealed class ScoreOwnerRowDto
	{
		public int UserId { get; set; }
		public int Mode { get; set; }

		/// <summary>Builds a <see cref="ScoreOwner" /> from this row, casting the stored mode column.</summary>
		/// <returns>The domain score owner row.</returns>
		public ScoreOwner ToRow()
		{
			return new ScoreOwner(UserId, (GameMode)Mode);
		}
	}

	/// <summary>
	///     A mutable row DTO matching the full Scores SELECT's columns.
	/// </summary>
	private sealed class ScoreRowDto
	{
		public long Id { get; set; }
		public int? RoundId { get; set; }
		public int? Team { get; set; }
		public string MapMd5 { get; set; } = "";
		public long Score { get; set; }
		public double Accuracy { get; set; }
		public int MaxCombo { get; set; }
		public int Mods { get; set; }
		public int N300 { get; set; }
		public int N100 { get; set; }
		public int N50 { get; set; }
		public int NMiss { get; set; }
		public int NGeki { get; set; }
		public int NKatu { get; set; }
		public string Grade { get; set; } = "N";
		public int Mode { get; set; }
		public DateTime PlayTime { get; set; }
		public int TimeElapsed { get; set; }
		public int ClientFlags { get; set; }
		public int UserId { get; set; }
		public bool Perfect { get; set; }
		public string Checksum { get; set; } = "";
		public DateTime SubmittedAt { get; set; }

		/// <summary>
		///     Builds a <see cref="ScoreRow" /> from this row, casting the stored enum columns.
		/// </summary>
		/// <returns>The domain score row.</returns>
		public ScoreRow ToRow()
		{
			return new ScoreRow(
				Id, RoundId, (MatchTeam?)Team, MapMd5, Score, Accuracy, MaxCombo, (Mods)Mods, N300, N100, N50,
				NMiss, NGeki, NKatu, Grade, (GameMode)Mode, PlayTime, TimeElapsed, (ClientFlags)ClientFlags,
				UserId, Perfect, Checksum, SubmittedAt);
		}
	}
}