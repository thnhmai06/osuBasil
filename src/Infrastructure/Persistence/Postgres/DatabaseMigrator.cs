namespace Basil.Infrastructure.Persistence.Postgres;

/// <summary>Brings the database up to date with the schema the server was built against.</summary>
/// <remarks>Applies every migration the database has not seen yet, in order, and records what it applied in the database.</remarks>
internal sealed class DatabaseMigrator(Database database, IEnumerable<IModuleMigration> modules)
{
	/// <summary>Applies every migration the database has not seen yet.</summary>
	/// <param name="cancellationToken">A token that cancels the migration.</param>
	public async Task MigrateAsync(CancellationToken cancellationToken = default)
	{
		foreach (var module in modules)
			await module.MigrateAsync(database, cancellationToken);
	}
}