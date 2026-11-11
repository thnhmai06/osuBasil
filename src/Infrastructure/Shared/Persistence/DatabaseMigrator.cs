namespace Basil.Infrastructure.Shared.Persistence;

/// <summary>Brings the database up to date with the schema the server was built against.</summary>
/// <remarks>Applies every migration the database has not seen yet, in order, and records what it applied in the database.</remarks>
/// <param name="database">The database whose schema is updated.</param>
/// <param name="modules">The migrations, in dependency order.</param>
public sealed class DatabaseMigrator(Database database, IEnumerable<IModuleMigration> modules)
{
	/// <summary>Applies every migration the database has not seen yet.</summary>
	/// <param name="ct">A token that cancels the migration.</param>
	public async Task MigrateAsync(CancellationToken ct = default)
	{
		foreach (var module in modules)
			await module.MigrateAsync(database, ct);
	}
}