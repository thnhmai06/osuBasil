using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage;

/// <summary>Opens connections to the server's database.</summary>
internal sealed class Database
{
	private readonly DataPaths paths;

	/// <summary>Creates a database around the resolved data paths.</summary>
	/// <param name="paths">The paths the server stores files at.</param>
	public Database(DataPaths paths) => this.paths = paths;

	/// <summary>Opens a connection to the database, ready to run statements.</summary>
	/// <param name="cancellationToken">A token that cancels opening.</param>
	/// <returns>The open connection.</returns>
	public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
	{
		var connection = new SqliteConnection($"Data Source={paths.Database};Foreign Keys=True");
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

		await using var pragma = connection.CreateCommand();
		pragma.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";
		await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

		return connection;
	}
}
