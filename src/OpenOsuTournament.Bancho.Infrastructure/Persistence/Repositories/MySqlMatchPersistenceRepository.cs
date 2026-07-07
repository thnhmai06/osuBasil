using Dapper;
using MySqlConnector;
using OpenOsuTournament.Bancho.Application.Abstractions.Multiplayer;

namespace OpenOsuTournament.Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMatchPersistenceRepository" />
public sealed class MySqlMatchPersistenceRepository(string connectionString) : IMatchPersistenceRepository
{
    public async Task<int> CreateMatchAsync(
        string name, int mode, int winCondition, int teamType, int hostId, bool hasPublicHistory,
        DateTime createdAt, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        return await connection.QuerySingleAsync<int>(
            """
            INSERT INTO Matches (Name, Mode, WinCondition, TeamType, HostId, HasPublicHistory, CreatedAt)
            VALUES (@Name, @Mode, @WinCondition, @TeamType, @HostId, @HasPublicHistory, @CreatedAt);
            SELECT LAST_INSERT_ID();
            """,
            new { Name = name, Mode = mode, WinCondition = winCondition, TeamType = teamType, HostId = hostId, HasPublicHistory = hasPublicHistory, CreatedAt = createdAt });
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
            SELECT LAST_INSERT_ID();
            """,
            new { MatchId = matchId, RoundIndex = roundIndex, BeatmapId = beatmapId, MapMd5 = mapMd5, Mods = mods, StartedAt = startedAt });
    }

    public async Task SetRoundEndedAsync(int roundId, DateTime endedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE Rounds SET EndedAt = @EndedAt WHERE Id = @RoundId",
            new { RoundId = roundId, EndedAt = endedAt });
    }

    private MySqlConnection Connect()
    {
        return new MySqlConnection(connectionString);
    }
}
