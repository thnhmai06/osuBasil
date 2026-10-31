using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Content.Persistence;

internal sealed class ContentDbContext(DbContextOptions<ContentDbContext> options) : DbContext(options),
	IDatabaseContext<ContentDbContext>, IWriteTicketContext
{
	public DbSet<SettingRow> Settings => Set<SettingRow>();
	public DbSet<MenuBannerRow> MenuBanners => Set<MenuBannerRow>();

	static string IDatabaseContext<ContentDbContext>.HistoryTable => "__ef_migrations_content";

	static ContentDbContext IDatabaseContext<ContentDbContext>.Create(DbContextOptions<ContentDbContext> options)
	{
		return new ContentDbContext(options);
	}

	public DbSet<WriteTicketRow> WriteTickets => Set<WriteTicketRow>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.ApplyModuleConfigurations<ContentDbContext>(modelBuilder);
		ContextModel.MapWriteTicketReference(modelBuilder);
	}
}