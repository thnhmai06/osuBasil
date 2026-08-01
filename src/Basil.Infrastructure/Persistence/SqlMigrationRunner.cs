using System.Reflection;
using DbUp;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence;

/// <summary>
///     Applies the embedded SQL migration scripts (see Persistence/Migrations/) against the SQLite
///     database file, in filename order, tracked via DbUp's own journal table so each script only
///     ever runs once per database file.
/// </summary>
public static class SqlMigrationRunner
{
	/// <summary>
	///     Applies every pending embedded migration script to the database file at the given
	///     connection string.
	/// </summary>
	/// <param name="connectionString">The SQLite connection string of the database file to migrate.</param>
	/// <remarks>
	///     Turns on WAL journal mode first. The mode persists in the database file header, so this
	///     only needs to happen once per file, but running it every startup is harmless and keeps a
	///     hand-copied or older database file in WAL mode too. Migration scripts are discovered as
	///     embedded resources and applied in filename order through DbUp, tracked in DbUp's journal
	///     so each script runs exactly once per database file.
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