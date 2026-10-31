using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Users.Persistence;

internal sealed class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContext(options),
	IDatabaseContext<UsersDbContext>, IWriteTicketContext
{
	public DbSet<UserRow> Users => Set<UserRow>();
	public DbSet<RestrictionRow> Restrictions => Set<RestrictionRow>();
	public DbSet<ModerationLogRow> ModerationLog => Set<ModerationLogRow>();
	public DbSet<RelationshipRow> Relationships => Set<RelationshipRow>();

	static string IDatabaseContext<UsersDbContext>.HistoryTable => "__ef_migrations_users";

	static UsersDbContext IDatabaseContext<UsersDbContext>.Create(DbContextOptions<UsersDbContext> options)
	{
		return new UsersDbContext(options);
	}

	public DbSet<WriteTicketRow> WriteTickets => Set<WriteTicketRow>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.ApplyModuleConfigurations<UsersDbContext>(modelBuilder);
		ContextModel.MapWriteTicketReference(modelBuilder);
	}
}