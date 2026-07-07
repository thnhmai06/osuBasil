using MySqlConnector;
using OpenOsuTournament.Bancho.Infrastructure.Persistence;
using Testcontainers.MySql;

namespace OpenOsuTournament.Bancho.Infrastructure.Tests.Persistence;

/// <summary>Verifies the migration runner applies Persistence/Migrations/001_base.sql against a real MySQL instance.</summary>
public class SqlMigrationRunnerTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.0").Build();

    public Task InitializeAsync()
    {
        return _mysql.StartAsync();
    }

    public Task DisposeAsync()
    {
        return _mysql.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task RunMigrations_CreatesExpectedTables()
    {
        var connectionString = _mysql.GetConnectionString();

        SqlMigrationRunner.RunMigrations(connectionString);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var table in new[]
                     { "Users", "UserStats", "Scores", "Channels", "ClientHashes", "IngameLogins", "Mail", "Matches", "Rounds", "Beatmaps", "Mapsets" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @table";
            command.Parameters.AddWithValue("@table", table);
            var count = Convert.ToInt32(await command.ExecuteScalarAsync());

            Assert.True(count == 1, $"expected table '{table}' to exist");
        }
    }

    [Fact]
    public async Task RunMigrations_SeedsBanchoBotAndDefaultChannels()
    {
        var connectionString = _mysql.GetConnectionString();

        SqlMigrationRunner.RunMigrations(connectionString);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await using var userCommand = connection.CreateCommand();
        userCommand.CommandText = "SELECT Name FROM Users WHERE Id = 1";
        var botName = (string?)await userCommand.ExecuteScalarAsync();
        Assert.Equal("BanchoBot", botName);

        await using var channelCommand = connection.CreateCommand();
        channelCommand.CommandText = "SELECT COUNT(*) FROM Channels WHERE Name = '#osu'";
        var channelCount = Convert.ToInt32(await channelCommand.ExecuteScalarAsync());
        Assert.Equal(1, channelCount);
    }
}