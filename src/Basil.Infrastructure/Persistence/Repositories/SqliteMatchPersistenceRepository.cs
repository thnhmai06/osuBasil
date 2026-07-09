using Basil.Application.Abstractions.Multiplayer;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMatchPersistenceRepository" />
public sealed class SqliteMatchPersistenceRepository(string connectionString) : IMatchPersistenceRepository
{
    public async Task<int> CreateMatchAsync(
        string name, int mode, int winCondition, int teamType, int hostId,
        DateTime createdAt, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        return await connection.QuerySingleAsync<int>(
            """
            INSERT INTO Matches (Name, Mode, WinCondition, TeamType, HostId, CreatedAt)
            VALUES (@Name, @Mode, @WinCondition, @TeamType, @HostId, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                Name = name, Mode = mode, WinCondition = winCondition, TeamType = teamType, HostId = hostId,
                CreatedAt = createdAt
            });
    }

    public async Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE Matches SET EndedAt = @EndedAt WHERE Id = @MatchId",
            new { MatchId = matchId, EndedAt = endedAt });
    }

    public async Task<int> CreateRoundAsync(
        int matchId, int roundIndex, int beatmapId, string mapMd5, int mods, DateTime startedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        return await connection.QuerySingleAsync<int>(
            """
            INSERT INTO Rounds (MatchId, RoundIndex, BeatmapId, MapMd5, Mods, StartedAt)
            VALUES (@MatchId, @RoundIndex, @BeatmapId, @MapMd5, @Mods, @StartedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                MatchId = matchId, RoundIndex = roundIndex, BeatmapId = beatmapId, MapMd5 = mapMd5, Mods = mods,
                StartedAt = startedAt
            });
    }

    public async Task SetRoundEndedAsync(int roundId, DateTime endedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE Rounds SET EndedAt = @EndedAt WHERE Id = @RoundId",
            new { RoundId = roundId, EndedAt = endedAt });
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
        await connection.ExecuteAsync("DELETE FROM Rounds WHERE MatchId = @MatchId", new { MatchId = matchId },
            transaction);
        await connection.ExecuteAsync("DELETE FROM Matches WHERE Id = @MatchId", new { MatchId = matchId },
            transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    private SqliteConnection Connect()
    {
        return new SqliteConnection(connectionString);
    }

    // Mutable DTOs so Dapper maps by property name (coercing column types loosely, e.g. SQLite's
    // Int64/string column values into the narrower int/DateTime properties below) instead of the
    // strict positional-constructor-type matching it requires for records like MatchRow/RoundRow.
    private sealed class MatchRowDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Mode { get; set; }
        public int WinCondition { get; set; }
        public int TeamType { get; set; }
        public int HostId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        public MatchRow ToRow()
        {
            return new MatchRow(Id, Name, Mode, WinCondition, TeamType, HostId, CreatedAt, EndedAt);
        }
    }

    private sealed class RoundRowDto
    {
        public int Id { get; set; }
        public int MatchId { get; set; }
        public int RoundIndex { get; set; }
        public int BeatmapId { get; set; }
        public string MapMd5 { get; set; } = "";
        public int Mods { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        public RoundRow ToRow()
        {
            return new RoundRow(Id, MatchId, RoundIndex, BeatmapId, MapMd5, Mods, StartedAt, EndedAt);
        }
    }
}
