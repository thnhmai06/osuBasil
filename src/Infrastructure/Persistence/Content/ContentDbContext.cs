using Basil.Common.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Content;

internal sealed class ContentDbContext(DbContextOptions<ContentDbContext> options) : DbContext(options),
	IDatabaseContext<ContentDbContext>, IWriteTicketContext
{
	public DbSet<SettingRow> Settings => Set<SettingRow>();
	public DbSet<MenuBannerRow> MenuBanners => Set<MenuBannerRow>();
	public DbSet<WriteTicketRow> WriteTickets => Set<WriteTicketRow>();

	static string IDatabaseContext<ContentDbContext>.HistoryTable => "__ef_migrations_content";

	static ContentDbContext IDatabaseContext<ContentDbContext>.Create(DbContextOptions<ContentDbContext> options) =>
		new(options);

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.ApplyModuleConfigurations<ContentDbContext>(modelBuilder);
		ContextModel.MapWriteTicketReference(modelBuilder);
	}
}