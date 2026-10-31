using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Common.Persistence;

/// <summary>Stores the common records used to coordinate writes across modules.</summary>
/// <param name="options">The options that configure the database context.</param>
public sealed class CommonDbContext(DbContextOptions<CommonDbContext> options) : DbContext(options),
	IDatabaseContext<CommonDbContext>, IWriteTicketContext
{
	static string IDatabaseContext<CommonDbContext>.HistoryTable => "__ef_migrations_common";

	static CommonDbContext IDatabaseContext<CommonDbContext>.Create(DbContextOptions<CommonDbContext> options)
	{
		return new CommonDbContext(options);
	}

	/// <summary>Gets the records that identify committed writes.</summary>
	public DbSet<WriteTicketRow> WriteTickets => Set<WriteTicketRow>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.ApplyConfiguration(new WriteTicketRowConfiguration());
	}
}