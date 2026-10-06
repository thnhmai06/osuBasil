using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage;

/// <summary>Opens connections to the server's database.</summary>
internal sealed class Database(DataPaths paths)
{
	/// <summary>Opens a connection to the database, ready to run statements.</summary>
	/// <param name="cancellationToken">A token that cancels opening.</param>
	/// <returns>The open connection.</returns>
	public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
	{
		var connection = new SqliteConnection($"Data Source={paths.Database};Foreign Keys=True");
		await connection.OpenAsync(cancellationToken);

		await using var pragma = connection.CreateCommand();
		pragma.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";
		await pragma.ExecuteNonQueryAsync(cancellationToken);

		return connection;
	}
}
