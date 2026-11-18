using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Shared.Persistence;

internal sealed class SharedDbContext(DbContextOptions<SharedDbContext> options) : DbContext(options),
	IDatabaseContext<SharedDbContext>
{
	public DbSet<SettingRow> Settings => Set<SettingRow>();

	static string IDatabaseContext<SharedDbContext>.HistoryTable => "__ef_migrations_shared";

	static SharedDbContext IDatabaseContext<SharedDbContext>.Create(DbContextOptions<SharedDbContext> options)
	{
		return new SharedDbContext(options);
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.ApplyModuleConfigurations<SharedDbContext>(modelBuilder);
	}
}