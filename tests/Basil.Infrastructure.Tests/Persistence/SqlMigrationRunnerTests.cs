using Basil.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>Verifies the migration runner applies Persistence/Migrations/001_base.sql against a real SQLite file.</summary>
public class SqlMigrationRunnerTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"basil-migration-test-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={_dbPath};Foreign Keys=True;Default Timeout=5";

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        File.Delete(_dbPath + "-wal");
        File.Delete(_dbPath + "-shm");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RunMigrations_CreatesExpectedTables()
    {
        SqlMigrationRunner.RunMigrations(ConnectionString);

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        foreach (var table in new[]
                 {
                     "Users", "UserStats", "Scores", "Channels", "ClientHashes", "IngameLogins", "Matches",
                     "Rounds", "Beatmaps", "Mapsets"
                 })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table";
            command.Parameters.AddWithValue("@table", table);
            var count = Convert.ToInt32(await command.ExecuteScalarAsync());

            Assert.True(count == 1, $"expected table '{table}' to exist");
        }
    }

    [Fact]
    public async Task RunMigrations_SeedsBasilBotAndDefaultChannels()
    {
        SqlMigrationRunner.RunMigrations(ConnectionString);

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        await using var userCommand = connection.CreateCommand();
        userCommand.CommandText = "SELECT Name FROM Users WHERE Id = 0";
        var botName = (string?)await userCommand.ExecuteScalarAsync();
        Assert.Equal("BasilBot", botName);

        await using var channelCommand = connection.CreateCommand();
        channelCommand.CommandText = "SELECT COUNT(*) FROM Channels WHERE Name = '#osu'";
        var channelCount = Convert.ToInt32(await channelCommand.ExecuteScalarAsync());
        Assert.Equal(1, channelCount);
    }
}
