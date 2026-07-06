using MySqlConnector;
using Testcontainers.MySql;

namespace Bancho.Infrastructure.Tests.Persistence;

/// <summary>
/// Verifies the migration runner applies migrations/base.sql (copied verbatim from bancho.py —
/// see Persistence/Migrations/001_base.sql) against a real MySQL instance. bancho.py's own
/// docker-compose mounts base.sql as the MySQL container's init script for fresh installs
/// (migrations.sql is only replayed against existing, upgrading databases) — so a single script
/// run is the correct fresh-install behavior, not a simplification.
/// </summary>
public class SqlMigrationRunnerTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.0").Build();

    public Task InitializeAsync() => _mysql.StartAsync();

    public Task DisposeAsync() => _mysql.DisposeAsync().AsTask();

    [Fact]
    public async Task RunMigrations_CreatesExpectedTables()
    {
        var connectionString = _mysql.GetConnectionString();

        Bancho.Infrastructure.Persistence.SqlMigrationRunner.RunMigrations(connectionString);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var table in new[] { "users", "stats", "scores", "channels", "client_hashes", "ingame_logins", "mail" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @table";
            command.Parameters.AddWithValue("@table", table);
            var count = Convert.ToInt32(await command.ExecuteScalarAsync());

            Assert.True(count == 1, $"expected table '{table}' to exist");
        }
    }

    [Fact]
    public async Task RunMigrations_SeedsBanchoBotAndDefaultChannels()
    {
        var connectionString = _mysql.GetConnectionString();

        Bancho.Infrastructure.Persistence.SqlMigrationRunner.RunMigrations(connectionString);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await using var userCommand = connection.CreateCommand();
        userCommand.CommandText = "SELECT name FROM users WHERE id = 1";
        var botName = (string?)await userCommand.ExecuteScalarAsync();
        Assert.Equal("BanchoBot", botName);

        await using var channelCommand = connection.CreateCommand();
        channelCommand.CommandText = "SELECT COUNT(*) FROM channels WHERE name = '#osu'";
        var channelCount = Convert.ToInt32(await channelCommand.ExecuteScalarAsync());
        Assert.Equal(1, channelCount);
    }
}
