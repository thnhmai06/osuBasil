using Basil.Application.Abstractions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMatchRepository" />
/// <remarks>
///     Rows map through the private mutable <c>*RowDto</c> DTOs; the stored integer enum columns
///     are cast to their domain enum types during mapping. Each method opens its own connection.
/// </remarks>
public sealed class SqliteMatchRepository(
	string connectionString,
	ILogger<SqliteMatchRepository> logger) : IMatchRepository
{
	/// <inheritdoc />
	/// <remarks>
	///     The insert and the id read-back are one batched statement, so the returned id is the
	///     immediately inserted row's.
	/// </remarks>
	public async Task<int> CreateMatchAsync(
		string name, DateTime createdAt, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var id = await connection.QuerySingleAsync<int>(
			"""
			INSERT INTO Matches (Name, CreatedAt)
			VALUES (@Name, @CreatedAt);
			SELECT last_insert_rowid();
			""",
			new { Name = name, CreatedAt = createdAt });
		logger.LogDebug("Match row created: Id={Id}", id);
		return id;
	}

	/// <inheritdoc />
	public async Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync(
			"UPDATE Matches SET EndedAt = @EndedAt WHERE Id = @MatchId",
			new { MatchId = matchId, EndedAt = endedAt });
		logger.LogDebug("Match row ended: MatchId={MatchId}", matchId);
	}

	/// <inheritdoc />
	/// <remarks>
	///     The enum columns are stored as their integer values. The insert and the id read-back are
	///     one batched statement, so the returned id is the immediately inserted row's.
	/// </remarks>
	public async Task<int> CreateRoundAsync(
		int matchId, int roundIndex, string mapMd5,
		GameMode mode, MatchWinCondition winCondition, MatchTeamType teamType,
		Mods mods, DateTime startedAt,
		CancellationToken cancellationToken = default)
	{
		return await SqliteInstrumentation.RecordAsync("match.round.create", async () =>
		{
			await using var connection = Connect();
			var id = await connection.QuerySingleAsync<int>(
				"""
				INSERT INTO Rounds (MatchId, RoundIndex, MapMd5, Mode, WinCondition, TeamType, Mods, StartedAt)
				VALUES (@MatchId, @RoundIndex, @MapMd5, @Mode, @WinCondition, @TeamType, @Mods, @StartedAt);
				SELECT last_insert_rowid();
				""",
				new
				{
					MatchId = matchId,
					RoundIndex = roundIndex,
					MapMd5 = mapMd5,
					Mode = mode,
					WinCondition = winCondition,
					TeamType = teamType,
					Mods = mods,
					StartedAt = startedAt
				});
			logger.LogDebug("Round row created: Id={Id} MatchId={MatchId}", id, matchId);
			return id;
		});
	}

	/// <inheritdoc />
	public async Task SetRoundEndedAsync(int roundId, DateTime endedAt, bool aborted,
		CancellationToken cancellationToken = default)
	{
		await SqliteInstrumentation.RecordAsync("match.round.end", async () =>
		{
			await using var connection = Connect();
			await connection.ExecuteAsync(
				"UPDATE Rounds SET EndedAt = @EndedAt, Aborted = @Aborted WHERE Id = @RoundId",
				new { RoundId = roundId, EndedAt = endedAt, Aborted = aborted });
			logger.LogDebug("Round row ended: RoundId={RoundId} Aborted={Aborted}", roundId, aborted);
		});
	}

	/// <inheritdoc />
	public async Task<Match?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var row = await connection.QuerySingleOrDefaultAsync<MatchRowDto>(
			"SELECT * FROM Matches WHERE Id = @MatchId", new { MatchId = matchId });
		return row?.ToRow();
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Round>> FetchRoundsAsync(int matchId,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var rows = await connection.QueryAsync<RoundRowDto>(
			"SELECT * FROM Rounds WHERE MatchId = @MatchId ORDER BY RoundIndex ASC", new { MatchId = matchId });
		return [.. rows.Select(r => r.ToRow())];
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Match>> FetchAllMatchesAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var rows = await connection.QueryAsync<MatchRowDto>("SELECT * FROM Matches ORDER BY Id DESC");
		return [.. rows.Select(r => r.ToRow())];
	}

	/// <inheritdoc />
	/// <remarks>
	///     Deletes the match's scores (via a subquery over its rounds), events, rounds, and the
	///     match row itself inside a single transaction.
	/// </remarks>
	public async Task DeleteMatchAsync(int matchId, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.OpenAsync(cancellationToken);
		await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
		await connection.ExecuteAsync(
			"DELETE FROM Scores WHERE RoundId IN (SELECT Id FROM Rounds WHERE MatchId = @MatchId)",
			new { MatchId = matchId }, transaction);
		await connection.ExecuteAsync("DELETE FROM MatchEvents WHERE MatchId = @MatchId",
			new { MatchId = matchId }, transaction);
		await connection.ExecuteAsync("DELETE FROM Rounds WHERE MatchId = @MatchId", new { MatchId = matchId },
			transaction);
		await connection.ExecuteAsync("DELETE FROM Matches WHERE Id = @MatchId", new { MatchId = matchId },
			transaction);
		await transaction.CommitAsync(cancellationToken);
		logger.LogDebug("Match row deleted (with scores/events/rounds): MatchId={MatchId}", matchId);
	}

	/// <inheritdoc />
	/// <remarks>
	///     The event type is stored as its integer value.
	/// </remarks>
	public async Task CreateEventAsync(MatchEvent row, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync(
			"""
			INSERT INTO MatchEvents (MatchId, EventType, ActorUserId, ActorUserName, TargetUserId, TargetUserName, Timestamp, Detail)
			VALUES (@MatchId, @EventType, @ActorUserId, @ActorUserName, @TargetUserId, @TargetUserName, @Timestamp, @Detail)
			""",
			new
			{
				row.MatchId,
				row.EventType,
				row.ActorUserId,
				row.ActorUserName,
				row.TargetUserId,
				row.TargetUserName,
				row.Timestamp,
				row.Detail
			});
		logger.LogDebug("MatchEvent row created: MatchId={MatchId} EventType={EventType}", row.MatchId, row.EventType);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<MatchEvent>> FetchEventsAsync(int matchId,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var rows = await connection.QueryAsync<MatchEventRowDto>(
			"SELECT * FROM MatchEvents WHERE MatchId = @MatchId ORDER BY Timestamp ASC, Id ASC",
			new { MatchId = matchId });
		return [.. rows.Select(r => r.ToRow())];
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Match>> FetchUnrecoveredMatchesAsync(
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var rows = await connection.QueryAsync<MatchRowDto>(
			"SELECT * FROM Matches WHERE EndedAt IS NULL ORDER BY Id ASC");
		return [.. rows.Select(r => r.ToRow())];
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Round>> FetchUnrecoveredRoundsAsync(int matchId,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var rows = await connection.QueryAsync<RoundRowDto>(
			"SELECT * FROM Rounds WHERE MatchId = @MatchId AND EndedAt IS NULL ORDER BY RoundIndex ASC",
			new { MatchId = matchId });
		return [.. rows.Select(r => r.ToRow())];
	}

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return new SqliteConnection(connectionString);
	}

	/// <summary>
	///     A mutable row DTO matching the Matches table columns.
	/// </summary>
	private sealed class MatchRowDto
	{
		public int Id { get; set; }
		public string Name { get; set; } = "";
		public DateTime CreatedAt { get; set; }
		public DateTime? EndedAt { get; set; }

		/// <summary>Builds a <see cref="Match" /> from this row.</summary>
		/// <returns>The domain match row.</returns>
		public Match ToRow()
		{
			return new Match(Id, Name, CreatedAt, EndedAt);
		}
	}

	/// <summary>
	///     A mutable row DTO matching the Rounds table columns.
	/// </summary>
	private sealed class RoundRowDto
	{
		public int Id { get; set; }
		public int MatchId { get; set; }
		public int RoundIndex { get; set; }
		public string MapMd5 { get; set; } = "";
		public int Mode { get; set; }
		public int WinCondition { get; set; }
		public int TeamType { get; set; }
		public bool Aborted { get; set; }
		public int Mods { get; set; }
		public DateTime StartedAt { get; set; }
		public DateTime? EndedAt { get; set; }

		/// <summary>
		///     Builds a <see cref="Round" /> from this row, casting the stored enum columns.
		/// </summary>
		/// <returns>The domain round row.</returns>
		public Round ToRow()
		{
			return new Round(
				Id, MatchId, RoundIndex, MapMd5, (GameMode)Mode, (MatchWinCondition)WinCondition,
				(MatchTeamType)TeamType, Aborted, (Mods)Mods, StartedAt, EndedAt);
		}
	}

	/// <summary>
	///     A mutable row DTO matching the MatchEvents table columns.
	/// </summary>
	private sealed class MatchEventRowDto
	{
		public int Id { get; set; }
		public int MatchId { get; set; }
		public int EventType { get; set; }
		public int? ActorUserId { get; set; }
		public string? ActorUserName { get; set; }
		public int? TargetUserId { get; set; }
		public string? TargetUserName { get; set; }
		public DateTime Timestamp { get; set; }
		public string? Detail { get; set; }

		/// <summary>Builds a <see cref="MatchEvent" /> from this row.</summary>
		/// <returns>The domain match event row.</returns>
		public MatchEvent ToRow()
		{
			return new MatchEvent(
				MatchId, EventType, ActorUserId, ActorUserName, TargetUserId, TargetUserName, Timestamp, Detail);
		}
	}
}