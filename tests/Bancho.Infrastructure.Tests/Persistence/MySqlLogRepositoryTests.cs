using Bancho.Infrastructure.Persistence.Repositories;
using Dapper;
using MySqlConnector;

namespace Bancho.Infrastructure.Tests.Persistence;

/// <summary>Ported from app/repositories/logs.py, scoped to the single append-only insert ClientIntegrityService needs.</summary>
public class MySqlLogRepositoryTests : IClassFixture<MySqlFixture>
{
    private readonly MySqlFixture _fixture;
    private readonly MySqlLogRepository _repository;

    public MySqlLogRepositoryTests(MySqlFixture fixture)
    {
        _fixture = fixture;
        _repository = new MySqlLogRepository(fixture.ConnectionString);
    }

    [Fact]
    public async Task CreateAsync_InsertsARowReadableBackFromTheLogsTable()
    {
        await _repository.CreateAsync(0, 42, "lastfm_flag", "hq!osu running (HqAssembly)");

        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        var row = await connection.QuerySingleAsync<(int From, int To, string Action, string Msg)>(
            "SELECT `from` AS `From`, `to` AS `To`, `action` AS `Action`, msg AS Msg FROM logs WHERE `to` = 42 AND `action` = 'lastfm_flag'");

        Assert.Equal(0, row.From);
        Assert.Equal(42, row.To);
        Assert.Equal("lastfm_flag", row.Action);
        Assert.Equal("hq!osu running (HqAssembly)", row.Msg);
    }
}