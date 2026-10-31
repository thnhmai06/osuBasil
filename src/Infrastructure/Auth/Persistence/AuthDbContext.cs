using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Auth.Persistence;

internal sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options),
	IDatabaseContext<AuthDbContext>, IWriteTicketContext
{
	public DbSet<CredentialRow> Credentials => Set<CredentialRow>();
	public DbSet<SessionRow> Sessions => Set<SessionRow>();

	static string IDatabaseContext<AuthDbContext>.HistoryTable => "__ef_migrations_auth";

	static AuthDbContext IDatabaseContext<AuthDbContext>.Create(DbContextOptions<AuthDbContext> options)
	{
		return new AuthDbContext(options);
	}

	public DbSet<WriteTicketRow> WriteTickets => Set<WriteTicketRow>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.MapUsersReference(modelBuilder);
		ContextModel.ApplyModuleConfigurations<AuthDbContext>(modelBuilder);
		ContextModel.MapWriteTicketReference(modelBuilder);
	}
}