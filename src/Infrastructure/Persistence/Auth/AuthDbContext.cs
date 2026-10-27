using Basil.Common.Infrastructure.Postgres;
using Basil.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Auth;

internal sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options),
	IDatabaseContext<AuthDbContext>, IWriteTicketContext
{
	public DbSet<CredentialRow> Credentials => Set<CredentialRow>();
	public DbSet<LoginRow> Logins => Set<LoginRow>();
	public DbSet<WriteTicketRow> WriteTickets => Set<WriteTicketRow>();

	static string IDatabaseContext<AuthDbContext>.HistoryTable => "__ef_migrations_auth";

	static AuthDbContext IDatabaseContext<AuthDbContext>.Create(DbContextOptions<AuthDbContext> options) =>
		new(options);

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		UserContextModel.MapUserReference(modelBuilder);
		ContextModel.ApplyModuleConfigurations<AuthDbContext>(modelBuilder);
		ContextModel.MapWriteTicketReference(modelBuilder);
	}
}