using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Common.Persistence;

/// <summary>The migrations of one module's context.</summary>
public interface IModuleMigration
{
	/// <summary>Applies the module's pending migrations.</summary>
	/// <param name="database">The shared database.</param>
	/// <param name="cancellationToken">A token that cancels the migration.</param>
	Task MigrateAsync(Database database, CancellationToken cancellationToken);
}

/// <summary>Migrates the context of a module; the migrator runs the modules in the order it was given them.</summary>
/// <typeparam name="TContext">The module's context.</typeparam>
public sealed class ModuleMigration<TContext> : IModuleMigration
	where TContext : DbContext, IDatabaseContext<TContext>
{
	/// <inheritdoc />
	public async Task MigrateAsync(Database database, CancellationToken cancellationToken)
	{
		await using var context = database.CreateConfirmedContext<TContext>();
		await context.Database.MigrateAsync(cancellationToken);
	}
}