using System.Data.Common;
using Npgsql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Basil.Testing.Persistence;

/// <summary>
///     Gives the database server of the test run, where tests create their own database. The engine is chosen by
///     <c>BASIL_TEST_PROVIDER</c> (<c>PostgreSQL</c>, the default, or <c>MySql</c>); the server is the one named by
///     <c>BASIL_TEST_POSTGRES</c> or <c>BASIL_TEST_MYSQL</c> (a connection string) when it is set, otherwise a container
///     started for the run.
/// </summary>
public sealed class PersistenceFixture : IAsyncLifetime
{
	private readonly PostgreSqlContainer? _postgres;
	private readonly MySqlContainer? _mysql;
	private readonly string? _server;

	public PersistenceFixture()
	{
		_server = Environment.GetEnvironmentVariable(TestServer.IsMySql ? "BASIL_TEST_MYSQL" : "BASIL_TEST_POSTGRES");
		if (!string.IsNullOrEmpty(_server)) return;

		if (TestServer.IsMySql)
			_mysql = new MySqlBuilder("mysql:8").WithUsername("root").WithPassword("basil").Build();
		else
			_postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
	}

	private string ServerConnectionString => !string.IsNullOrEmpty(_server) ? _server
		: _mysql is not null ? _mysql.GetConnectionString() : _postgres!.GetConnectionString();

	public async ValueTask InitializeAsync()
	{
		if (_postgres is not null) await _postgres.StartAsync();
		if (_mysql is not null) await _mysql.StartAsync();
	}

	public async ValueTask DisposeAsync()
	{
		if (_postgres is not null) await _postgres.DisposeAsync();
		if (_mysql is not null) await _mysql.DisposeAsync();
	}

	/// <summary>Creates an empty database and returns how to reach it.</summary>
	/// <param name="name">The database's name; the name is generated, never taken from outside.</param>
	/// <returns>The connection string of the new database.</returns>
	public async ValueTask<string> CreateDatabaseAsync(string name)
	{
		await using var connection = await TestServer.OpenAsync(ServerConnectionString, CancellationToken.None);
		await Run(connection, TestServer.IsMySql ? $"create database `{name}`" : $"create database \"{name}\"");
		return TestServer.WithDatabase(ServerConnectionString, name);
	}

	/// <summary>Deletes a database created for a test class.</summary>
	/// <param name="name">The database's name.</param>
	public async ValueTask DropDatabaseAsync(string name)
	{
		await using var connection = await TestServer.OpenAsync(ServerConnectionString, CancellationToken.None);
		await Run(connection,
			TestServer.IsMySql ? $"drop database if exists `{name}`" : $"drop database if exists \"{name}\" with (force)");
	}

	private static async Task Run(DbConnection connection, string sql)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		await command.ExecuteNonQueryAsync();
	}
}
