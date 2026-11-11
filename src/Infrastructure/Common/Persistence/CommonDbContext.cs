using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Common.Persistence;

internal sealed class CommonDbContext(DbContextOptions<CommonDbContext> options) : DbContext(options),
	IDatabaseContext<CommonDbContext>
{
	/// <summary>The stored settings.</summary>
	public DbSet<SettingRow> Settings => Set<SettingRow>();

	static string IDatabaseContext<CommonDbContext>.HistoryTable => "__ef_migrations_common";

	static CommonDbContext IDatabaseContext<CommonDbContext>.Create(DbContextOptions<CommonDbContext> options)
	{
		return new CommonDbContext(options);
	}

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.ApplyModuleConfigurations<CommonDbContext>(modelBuilder);
	}
}