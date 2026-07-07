using Bancho.Infrastructure.Persistence;
using Testcontainers.MySql;

namespace Bancho.Infrastructure.Tests.Persistence;

/// <summary>Spins up a real MySQL container with migrations/base.sql applied, shared per test class.</summary>
public sealed class MySqlFixture : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.0").Build();

    public string ConnectionString => _mysql.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        SqlMigrationRunner.RunMigrations(ConnectionString);
    }

    public Task DisposeAsync()
    {
        return _mysql.DisposeAsync().AsTask();
    }
}