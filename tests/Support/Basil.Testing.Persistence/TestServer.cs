using System.Data.Common;
using Npgsql;

namespace Basil.Testing.Persistence;

/// <summary>Reaches the database server of the test run with plain ADO.NET, for tests that read or change tables directly.</summary>
public static class TestServer
{
	/// <summary>Opens a connection to a database.</summary>
	/// <param name="connectionString">How to reach the database.</param>
	/// <param name="cancellationToken">A token that cancels the opening.</param>
	/// <returns>The open connection.</returns>
	public static async ValueTask<DbConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
	{
		var connection = new NpgsqlConnection(connectionString);
		await connection.OpenAsync(cancellationToken);
		return connection;
	}
}
