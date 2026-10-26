using Npgsql;
using Testcontainers.PostgreSql;

namespace Basil.Infrastructure.Persistence.Tests.Support;

/// <summary>
///     Gives the PostgreSQL server of the test run, where tests create their own database: the server named by the
///     <c>BASIL_TEST_POSTGRES</c> connection string when it is set, otherwise a container started for the run.
/// </summary>
public sealed class PersistenceFixture : IAsyncLifetime
{
	private readonly PostgreSqlContainer? _container;
	private readonly string? _server = Environment.GetEnvironmentVariable("BASIL_TEST_POSTGRES");

	public PersistenceFixture()
	{
		if (string.IsNullOrEmpty(_server))
			_container = new PostgreSqlBuilder("postgres:16-alpine").Build();
	}

	private string ServerConnectionString => _server ?? _container!.GetConnectionString();

	public async ValueTask InitializeAsync()
	{
		if (_container is not null)
			await _container.StartAsync();
	}

	public async ValueTask DisposeAsync()
	{
		if (_container is not null)
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

		var builder = new NpgsqlConnectionStringBuilder(ServerConnectionString);
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
		var connection = new NpgsqlConnection(ServerConnectionString);
		await connection.OpenAsync();
		return connection;
	}
}