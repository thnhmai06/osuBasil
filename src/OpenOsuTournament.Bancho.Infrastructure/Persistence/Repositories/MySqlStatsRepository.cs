using Dapper;
using MySqlConnector;
using OpenOsuTournament.Bancho.Application.Abstractions.Users;

namespace OpenOsuTournament.Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IStatsRepository" />
public sealed class MySqlStatsRepository(string connectionString) : IStatsRepository
{
    public async Task<IReadOnlyList<Stats>> FetchAllForUserAsync(int userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<StatsRow>(
            "SELECT * FROM UserStats WHERE Id = @UserId",
            new { UserId = userId });
        return rows.Select(r => r.ToStats()).ToList();
    }

    public async Task<Stats?> FetchOneAsync(int userId, int mode, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<StatsRow>(
            "SELECT * FROM UserStats WHERE Id = @UserId AND Mode = @Mode",
            new { UserId = userId, Mode = mode });
        return row?.ToStats();
    }

    public async Task IncrementReplayViewsAsync(int userId, int mode, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE UserStats SET ReplayViews = ReplayViews + 1 WHERE Id = @UserId AND Mode = @Mode",
            new { UserId = userId, Mode = mode });
    }

    private MySqlConnection Connect()
    {
        return new MySqlConnection(connectionString);
    }

    private sealed class StatsRow
    {
        public int Id { get; set; }
        public int Mode { get; set; }
        public long Tscore { get; set; }
        public long Rscore { get; set; }
        public int Plays { get; set; }
        public int Playtime { get; set; }
        public double Acc { get; set; }
        public int MaxCombo { get; set; }
        public int TotalHits { get; set; }
        public int ReplayViews { get; set; }
        public int XhCount { get; set; }
        public int XCount { get; set; }
        public int ShCount { get; set; }
        public int SCount { get; set; }
        public int ACount { get; set; }

        public Stats ToStats()
        {
            return new Stats(
                Id, Mode, Tscore, Rscore, Plays, Playtime, Acc, MaxCombo, TotalHits, ReplayViews,
                XhCount, XCount, ShCount, SCount, ACount);
        }
    }
}
