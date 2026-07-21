using Basil.Infrastructure.Persistence.Repositories;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>Ported from app/repositories/logs.py, scoped to the single append-only insert ClientIntegrityService needs.</summary>
public class SqliteLogRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    private readonly SqliteLogRepository _repository = new(fixture.ConnectionString);

    [Fact]
    public async Task CreateAsync_InsertsARowReadableBackFromTheLogsTable()
    {
        await _repository.CreateAsync(0, 42, "lastfm_flag", "hq!osu running (HqAssembly)");

        await using var connection = new SqliteConnection(fixture.ConnectionString);
        var row = await connection.QuerySingleAsync<(int FromId, int ToId, string Action, string Msg)>(
            "SELECT FromId, ToId, Action, Msg FROM Logs WHERE ToId = 42 AND Action = 'lastfm_flag'");

        Assert.Equal(0, row.FromId);
        Assert.Equal(42, row.ToId);
        Assert.Equal("lastfm_flag", row.Action);
        Assert.Equal("hq!osu running (HqAssembly)", row.Msg);
    }
}