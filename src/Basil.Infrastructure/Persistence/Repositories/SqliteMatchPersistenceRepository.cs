using Basil.Application.Abstractions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMatchPersistenceRepository" />
public sealed class SqliteMatchPersistenceRepository(string connectionString) : IMatchPersistenceRepository
{
    public async Task<int> CreateMatchAsync(
        string name, DateTime createdAt, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        return await connection.QuerySingleAsync<int>(
            """
            INSERT INTO Matches (Name, CreatedAt)
            VALUES (@Name, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new { Name = name, CreatedAt = createdAt });
    }

    public async Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE Matches SET EndedAt = @EndedAt WHERE Id = @MatchId",
            new { MatchId = matchId, EndedAt = endedAt });
    }

    public async Task<int> CreateRoundAsync(
        int matchId, int roundIndex, string mapMd5,
        GameMode mode, MatchWinCondition winCondition, MatchTeamType teamType,
        Mods mods, DateTime startedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        return await connection.QuerySingleAsync<int>(
            """
            INSERT INTO Rounds (MatchId, RoundIndex, MapMd5, Mode, WinCondition, TeamType, Mods, StartedAt)
            VALUES (@MatchId, @RoundIndex, @MapMd5, @Mode, @WinCondition, @TeamType, @Mods, @StartedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                MatchId = matchId, RoundIndex = roundIndex, MapMd5 = mapMd5,
                Mode = mode, WinCondition = winCondition, TeamType = teamType,
                Mods = mods, StartedAt = startedAt
            });
    }

    public async Task SetRoundEndedAsync(int roundId, DateTime endedAt, bool aborted,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE Rounds SET EndedAt = @EndedAt, Aborted = @Aborted WHERE Id = @RoundId",
            new { RoundId = roundId, EndedAt = endedAt, Aborted = aborted });
    }

    public async Task<MatchRow?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<MatchRowDto>(
            "SELECT * FROM Matches WHERE Id = @MatchId", new { MatchId = matchId });
        return row?.ToRow();
    }

    public async Task<IReadOnlyList<RoundRow>> FetchRoundsAsync(int matchId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<RoundRowDto>(
            "SELECT * FROM Rounds WHERE MatchId = @MatchId ORDER BY RoundIndex ASC", new { MatchId = matchId });
        return rows.Select(r => r.ToRow()).ToList();
    }

    public async Task<IReadOnlyList<MatchRow>> FetchAllMatchesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<MatchRowDto>("SELECT * FROM Matches ORDER BY Id DESC");
        return rows.Select(r => r.ToRow()).ToList();
    }

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
    }

    public async Task CreateEventAsync(MatchEventRow row, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            """
            INSERT INTO MatchEvents (MatchId, EventType, ActorUserId, ActorUserName, TargetUserId, TargetUserName, Timestamp, Detail)
            VALUES (@MatchId, @EventType, @ActorUserId, @ActorUserName, @TargetUserId, @TargetUserName, @Timestamp, @Detail)
            """,
            new
            {
                row.MatchId, row.EventType, row.ActorUserId, row.ActorUserName,
                row.TargetUserId, row.TargetUserName, row.Timestamp, row.Detail
            });
    }

    public async Task<IReadOnlyList<MatchEventRow>> FetchEventsAsync(int matchId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<MatchEventRowDto>(
            "SELECT * FROM MatchEvents WHERE MatchId = @MatchId ORDER BY Timestamp ASC, Id ASC",
            new { MatchId = matchId });
        return rows.Select(r => r.ToRow()).ToList();
    }

    public async Task<IReadOnlyList<MatchRow>> FetchUnrecoveredMatchesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<MatchRowDto>(
            "SELECT * FROM Matches WHERE EndedAt IS NULL ORDER BY Id ASC");
        return rows.Select(r => r.ToRow()).ToList();
    }

    public async Task<IReadOnlyList<RoundRow>> FetchUnrecoveredRoundsAsync(int matchId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<RoundRowDto>(
            "SELECT * FROM Rounds WHERE MatchId = @MatchId AND EndedAt IS NULL ORDER BY RoundIndex ASC",
            new { MatchId = matchId });
        return rows.Select(r => r.ToRow()).ToList();
    }

    private SqliteConnection Connect()
    {
        return new SqliteConnection(connectionString);
    }

    private sealed class MatchRowDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        public MatchRow ToRow()
        {
            return new MatchRow(Id, Name, CreatedAt, EndedAt);
        }
    }

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

        public RoundRow ToRow()
        {
            return new RoundRow(Id, MatchId, RoundIndex, MapMd5,
                (GameMode)Mode, (MatchWinCondition)WinCondition, (MatchTeamType)TeamType,
                Aborted, (Mods)Mods, StartedAt, EndedAt);
        }
    }

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

        public MatchEventRow ToRow()
        {
            return new MatchEventRow(MatchId, EventType,
                ActorUserId, ActorUserName, TargetUserId, TargetUserName,
                Timestamp, Detail);
        }
    }
}
