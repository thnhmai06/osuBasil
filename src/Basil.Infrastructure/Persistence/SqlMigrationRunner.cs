using System.Reflection;
using DbUp;

namespace Basil.Infrastructure.Persistence;

/// <summary>
///     Applies the embedded SQL migration scripts (see Persistence/Migrations/) against a MySQL
///     database. Only base.sql is embedded — bancho.py's own docker-compose mounts base.sql as the
///     MySQL container's init script for fresh installs, while migrations.sql is exclusively a
///     historical changelog replayed against existing production databases upgrading from old
///     versions. A fresh Basil deployment needs only base.sql.
/// </summary>
public static class SqlMigrationRunner
{
    public static void RunMigrations(string connectionString)
    {
        var upgrader = DeployChanges.To
            .MySqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful) throw new InvalidOperationException("SQL migration failed.", result.Error);
    }
}