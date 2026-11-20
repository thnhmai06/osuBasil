using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Shared.Persistence;

public interface IModuleMigration
{
	Task MigrateAsync(Database database, CancellationToken ct);
}

public sealed class ModuleMigration<TContext> : IModuleMigration
	where TContext : DbContext, IDatabaseContext<TContext>
{
	public async Task MigrateAsync(Database database, CancellationToken ct)
	{
		await using var context = database.CreateConfirmedContext<TContext>();
		await context.Database.MigrateAsync(ct);
	}
}