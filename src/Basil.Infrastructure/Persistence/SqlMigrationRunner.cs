using System.Reflection;
using DbUp;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence;

/// <summary>
///     Applies the embedded SQL migration scripts (see <c>Persistence/Migrations/</c>) against the
///     SQLite database file, in filename order. Each script runs exactly once per database file.
/// </summary>
public static class SqlMigrationRunner
{
	/// <summary>
	///     Applies every pending embedded migration script to the database file at the given
	///     connection string.
	/// </summary>
	/// <param name="connectionString">The SQLite connection string of the database file to migrate.</param>
	/// <remarks>
	///     Switches the database to WAL journal mode first.
	/// </remarks>
	/// <exception cref="InvalidOperationException">A migration script failed; the inner exception carries the cause.</exception>
	public static void RunMigrations(string connectionString)
	{
		using (var connection = new SqliteConnection(connectionString))
		{
			connection.Open();
			using var pragma = connection.CreateCommand();
			pragma.CommandText = "PRAGMA journal_mode=WAL;";
			pragma.ExecuteNonQuery();
		}

		var upgrader = DeployChanges.To
			.SqliteDatabase(connectionString)
			.WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
			.LogToConsole()
			.Build();

		var result = upgrader.PerformUpgrade();

		if (!result.Successful) throw new InvalidOperationException("SQL migration failed.", result.Error);
	}
}