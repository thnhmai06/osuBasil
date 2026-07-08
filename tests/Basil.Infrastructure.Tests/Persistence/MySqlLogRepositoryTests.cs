using Basil.Infrastructure.Persistence.Repositories;
using Dapper;
using MySqlConnector;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>Ported from app/repositories/logs.py, scoped to the single append-only insert ClientIntegrityService needs.</summary>
public class MySqlLogRepositoryTests(MySqlFixture fixture) : IClassFixture<MySqlFixture>
{
    private readonly MySqlLogRepository _repository = new(fixture.ConnectionString);

    [Fact]
    public async Task CreateAsync_InsertsARowReadableBackFromTheLogsTable()
    {
        await _repository.CreateAsync(0, 42, "lastfm_flag", "hq!osu running (HqAssembly)");

        await using var connection = new MySqlConnection(fixture.ConnectionString);
        var row = await connection.QuerySingleAsync<(int From, int To, string Action, string Msg)>(
            "SELECT `From`, `To`, `Action`, Msg FROM Logs WHERE `To` = 42 AND `Action` = 'lastfm_flag'");

        Assert.Equal(0, row.From);
        Assert.Equal(42, row.To);
        Assert.Equal("lastfm_flag", row.Action);
        Assert.Equal("hq!osu running (HqAssembly)", row.Msg);
    }
}