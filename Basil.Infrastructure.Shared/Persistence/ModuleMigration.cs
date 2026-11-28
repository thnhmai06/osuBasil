using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Shared.Persistence;

public interface IModuleMigration
{
	string Name { get; }

	/// <summary>Names the first migration the database has applied that this program does not know, or null.</summary>
	Task<string?> FindUnknownAppliedAsync(Database database, CancellationToken ct);

	Task MigrateAsync(Database database, CancellationToken ct);
}

public sealed class ModuleMigration<TContext> : IModuleMigration
	where TContext : DbContext, IDatabaseContext<TContext>
{
	public string Name => typeof(TContext).Name;

	public async Task<string?> FindUnknownAppliedAsync(Database database, CancellationToken ct)
	{
		await using var context = database.CreateConfirmedContext<TContext>();
		var known = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
		var applied = await context.Database.GetAppliedMigrationsAsync(ct);
		return applied.FirstOrDefault(migration => !known.Contains(migration));
	}

	public async Task MigrateAsync(Database database, CancellationToken ct)
	{
		await using var context = database.CreateConfirmedContext<TContext>();
		await context.Database.MigrateAsync(ct);
	}
}