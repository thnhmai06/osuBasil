using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Common;

internal sealed class CommonDbContext(DbContextOptions<CommonDbContext> options) : DbContext(options),
	IDatabaseContext<CommonDbContext>, IWriteTicketContext
{
	public DbSet<WriteTicketRow> WriteTickets => Set<WriteTicketRow>();

	static string IDatabaseContext<CommonDbContext>.HistoryTable => "__ef_migrations_common";

	static CommonDbContext IDatabaseContext<CommonDbContext>.Create(DbContextOptions<CommonDbContext> options) =>
		new(options);

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.ApplyConfiguration(new WriteTicketRowConfiguration());
	}
}