using Bancho.Application.Abstractions;
using Dapper;
using MySqlConnector;

namespace Bancho.Infrastructure.Persistence;

/// <inheritdoc cref="IStatsRepository" />
public sealed class MySqlStatsRepository(string connectionString) : IStatsRepository
{
    // Mutable DTO — see MySqlUserRepository for why (Dapper record-constructor type strictness
    // vs MySqlConnector's driver-level type inference on tinyint/unsigned columns).
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

        public Stats ToStats() => new(
            Id, Mode, Tscore, Rscore, Plays, Playtime, Acc, MaxCombo, TotalHits, ReplayViews,
            XhCount, XCount, ShCount, SCount, ACount);
    }

    private const string SelectColumns = """
        id, mode, tscore, rscore, plays, playtime, acc, max_combo AS MaxCombo,
        total_hits AS TotalHits, replay_views AS ReplayViews, xh_count AS XhCount,
        x_count AS XCount, sh_count AS ShCount, s_count AS SCount, a_count AS ACount
        """;

    // bancho.py's schema uses tinyint(1) for `mode` (0-8, not a boolean) — MySqlConnector's
    // default TreatTinyAsBoolean=true would coerce any nonzero mode value to 1. Disable it.
    private readonly string _connectionString = connectionString + ";TreatTinyAsBoolean=false";

    private MySqlConnection Connect() => new(_connectionString);

    public async Task<IReadOnlyList<Stats>> FetchAllForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<StatsRow>(
            $"SELECT {SelectColumns} FROM stats WHERE id = @UserId",
            new { UserId = userId });
        return rows.Select(r => r.ToStats()).ToList();
    }

    public async Task<Stats?> FetchOneAsync(int userId, int mode, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<StatsRow>(
            $"SELECT {SelectColumns} FROM stats WHERE id = @UserId AND mode = @Mode",
            new { UserId = userId, Mode = mode });
        return row?.ToStats();
    }

    public async Task UpdateAfterScoreAsync(
        int userId,
        int mode,
        long tscore,
        long rscore,
        int plays,
        int playtime,
        double acc,
        int maxCombo,
        int totalHits,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            """
            UPDATE stats
            SET tscore = @Tscore, rscore = @Rscore, plays = @Plays, playtime = @Playtime,
                acc = @Acc, max_combo = @MaxCombo, total_hits = @TotalHits
            WHERE id = @UserId AND mode = @Mode
            """,
            new
            {
                UserId = userId, Mode = mode, Tscore = tscore, Rscore = rscore, Plays = plays,
                Playtime = playtime, Acc = acc, MaxCombo = maxCombo, TotalHits = totalHits,
            });
    }
}
