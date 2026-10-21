using Npgsql;
using Testcontainers.PostgreSql;

namespace Basil.Infrastructure.Persistence.Tests.Support;

/// <summary>Runs the PostgreSQL container of the test run; tests create their own database in it.</summary>
public sealed class PersistenceFixture : IAsyncLifetime
{
	private readonly PostgreSqlContainer _container;

	public PersistenceFixture() => _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

	public async ValueTask InitializeAsync()
	{
		await _container.StartAsync();
	}

	public async ValueTask DisposeAsync()
	{
		await _container.DisposeAsync();
	}

	/// <summary>Creates an empty database and returns how to reach it.</summary>
	/// <param name="name">The database's name; the name is generated, never taken from outside.</param>
	/// <returns>The connection string of the new database.</returns>
	public async ValueTask<string> CreateDatabaseAsync(string name)
	{
		await using var connection = await OpenRootConnectionAsync();
		await using var create = new NpgsqlCommand($"create database \"{name}\"", connection);
		await create.ExecuteNonQueryAsync();

		var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString());
		builder.Database = name;
		return builder.ConnectionString;
	}

	/// <summary>Deletes a database created for a test class.</summary>
	/// <param name="name">The database's name.</param>
	public async ValueTask DropDatabaseAsync(string name)
	{
		await using var connection = await OpenRootConnectionAsync();
		await using var drop = new NpgsqlCommand($"drop database if exists \"{name}\" with (force)", connection);
		await drop.ExecuteNonQueryAsync();
	}

	private async ValueTask<NpgsqlConnection> OpenRootConnectionAsync()
	{
		var connection = new NpgsqlConnection(_container.GetConnectionString());
		await connection.OpenAsync();
		return connection;
	}
}
