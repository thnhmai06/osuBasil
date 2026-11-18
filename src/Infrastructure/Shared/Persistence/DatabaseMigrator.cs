namespace Basil.Infrastructure.Shared.Persistence;

public sealed class DatabaseMigrator(Database database, IEnumerable<IModuleMigration> modules)
{
	public async Task MigrateAsync(CancellationToken ct = default)
	{
		foreach (var module in modules)
			await module.MigrateAsync(database, ct);
	}
}