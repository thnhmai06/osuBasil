using Basil.Application.Abstractions.Users;
using Basil.Domain.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IStatsRepository" />
public sealed class SqliteStatsRepository(string connectionString) : IStatsRepository
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

    private SqliteConnection Connect()
    {
        return new SqliteConnection(connectionString);
    }

    private sealed class StatsRow
    {
        public int Id { get; set; }
        public int Mode { get; set; }
        public long Tscore { get; set; }
        public long Rscore { get; set; }
        public int Plays { get; set; }
        public double Acc { get; set; }

        public Stats ToStats()
        {
            return new Stats(Id, (GameMode)Mode, Tscore, Rscore, Plays, Acc);
        }
    }
}
