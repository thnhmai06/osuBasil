using System.Data.Common;
using MySql.Data.MySqlClient;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Basil.Testing.Persistence;

public sealed class PersistenceFixture : IAsyncLifetime
{
	private readonly MySqlContainer? _mysql;
	private readonly PostgreSqlContainer? _postgres;
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

	public async ValueTask<string> CreateDatabaseAsync(string name)
	{
		await using var connection = await TestServer.OpenAsync(ServerConnectionString, CancellationToken.None);
		await Run(connection, TestServer.IsMySql ? $"create database `{name}`" : $"create database \"{name}\"");
		return TestServer.WithDatabase(ServerConnectionString, name);
	}

	public async ValueTask DropDatabaseAsync(string name)
	{
		// MySQL keeps the pooled connections of a dropped database until they idle out, and a run of many test classes
		// would exhaust the server's connections; PostgreSQL's force drop ends them itself.
		if (TestServer.IsMySql) MySqlConnection.ClearAllPools();
		await using var connection = await TestServer.OpenAsync(ServerConnectionString, CancellationToken.None);
		await Run(connection,
			TestServer.IsMySql
				? $"drop database if exists `{name}`"
				: $"drop database if exists \"{name}\" with (force)");
	}

	private static async Task Run(DbConnection connection, string sql)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		await command.ExecuteNonQueryAsync();
	}
}