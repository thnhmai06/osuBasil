using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Users;

internal sealed class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContext(options),
	IDatabaseContext<UsersDbContext>, IWriteTicketContext
{
	public DbSet<UserRow> Users => Set<UserRow>();
	public DbSet<RestrictionRow> Restrictions => Set<RestrictionRow>();
	public DbSet<ModerationLogRow> ModerationLog => Set<ModerationLogRow>();
	public DbSet<RelationshipRow> Relationships => Set<RelationshipRow>();
	public DbSet<WriteTicketRow> WriteTickets => Set<WriteTicketRow>();

	static string IDatabaseContext<UsersDbContext>.HistoryTable => "__ef_migrations_users";

	static UsersDbContext IDatabaseContext<UsersDbContext>.Create(DbContextOptions<UsersDbContext> options) =>
		new(options);

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.ApplyModuleConfigurations<UsersDbContext>(modelBuilder);
		ContextModel.MapWriteTicketReference(modelBuilder);
	}
}