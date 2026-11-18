using System.Data.Common;
using MySql.Data.MySqlClient;
using Npgsql;

namespace Basil.Testing.Persistence;

public static class TestServer
{
	public static bool IsMySql => string.Equals(Environment.GetEnvironmentVariable("BASIL_TEST_PROVIDER"), "MySql",
		StringComparison.OrdinalIgnoreCase);

	public static string ProviderName => IsMySql ? "MySql" : "PostgreSQL";

	public static async ValueTask<DbConnection> OpenAsync(string connectionString, CancellationToken ct)
	{
		DbConnection connection =
			IsMySql ? new MySqlConnection(connectionString) : new NpgsqlConnection(connectionString);
		await connection.OpenAsync(ct);
		return connection;
	}

	public static string WithDatabase(string connectionString, string database)
	{
		if (IsMySql)
			return new MySqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;
		return new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;
	}
}