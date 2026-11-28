using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Sessions.Persistence;

internal sealed class SessionsDbContext(DbContextOptions<SessionsDbContext> options) : DbContext(options),
	IDatabaseContext<SessionsDbContext>
{
	public DbSet<CredentialRow> Credentials => Set<CredentialRow>();

	public DbSet<SessionRow> Sessions => Set<SessionRow>();

	static string IDatabaseContext<SessionsDbContext>.HistoryTable => "__ef_migrations_sessions";

	static SessionsDbContext IDatabaseContext<SessionsDbContext>.Create(
		DbContextOptions<SessionsDbContext> options)
	{
		return new SessionsDbContext(options);
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.MapUsersReference(modelBuilder);
		ContextModel.ApplyModuleConfigurations<SessionsDbContext>(modelBuilder);
	}
}