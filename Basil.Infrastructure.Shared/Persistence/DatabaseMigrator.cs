namespace Basil.Infrastructure.Shared.Persistence;

public sealed class DatabaseMigrator(Database database, IEnumerable<IModuleMigration> modules)
{
	public async Task MigrateAsync(CancellationToken ct = default)
	{
		// A database written by a newer Basil is left alone: migrating it forward would hide that.
		foreach (var module in modules)
			if (await module.FindUnknownAppliedAsync(database, ct) is { } unknown)
				throw new InvalidOperationException(
					$"The database of {module.Name} has the migration {unknown}, which this Basil does not know. Update Basil, or restore a database made by this version.");

		foreach (var module in modules)
			await module.MigrateAsync(database, ct);
	}
}