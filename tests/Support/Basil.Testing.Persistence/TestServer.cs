using System.Data.Common;
using MySql.Data.MySqlClient;
using Npgsql;

namespace Basil.Testing.Persistence;

/// <summary>
///     Reaches the database server of the test run with plain ADO.NET, for tests that read or change tables directly.
///     The engine is the one named by <c>BASIL_TEST_PROVIDER</c>: <c>PostgreSQL</c> (the default) or <c>MySql</c>.
/// </summary>
public static class TestServer
{
	/// <summary>Gets whether the test run uses MySQL.</summary>
	public static bool IsMySql => string.Equals(Environment.GetEnvironmentVariable("BASIL_TEST_PROVIDER"), "MySql",
		StringComparison.OrdinalIgnoreCase);

	/// <summary>Gets the name of the engine, as the <c>Provider</c> setting spells it.</summary>
	public static string ProviderName => IsMySql ? "MySql" : "PostgreSQL";

	/// <summary>Opens a connection to a database.</summary>
	/// <param name="connectionString">How to reach the database.</param>
	/// <param name="cancellationToken">A token that cancels the opening.</param>
	/// <returns>The open connection.</returns>
	public static async ValueTask<DbConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
	{
		DbConnection connection =
			IsMySql ? new MySqlConnection(connectionString) : new NpgsqlConnection(connectionString);
		await connection.OpenAsync(cancellationToken);
		return connection;
	}

	/// <summary>Points a server connection string at one of its databases.</summary>
	/// <param name="connectionString">How to reach the server.</param>
	/// <param name="database">The name of the database.</param>
	/// <returns>The connection string of the database.</returns>
	public static string WithDatabase(string connectionString, string database)
	{
		if (IsMySql)
			return new MySqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;
		return new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;
	}
}