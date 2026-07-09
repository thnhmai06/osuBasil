using System.Reflection;
using DbUp;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence;

/// <summary>
///     Applies the embedded SQL migration scripts (see Persistence/Migrations/) against the SQLite
///     database file. Only base.sql is embedded — a fresh Basil deployment needs only base.sql, no
///     changelog of historical migrations exists yet.
/// </summary>
public static class SqlMigrationRunner
{
    public static void RunMigrations(string connectionString)
    {
        // journal_mode=WAL persists into the database file header, so this only needs to run once
        // per database file, but running it every startup is harmless and keeps a hand-copied/older
        // database file in WAL mode too.
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
