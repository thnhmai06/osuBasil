using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Authentication.Persistence;

internal sealed class AuthenticationDbContext(DbContextOptions<AuthenticationDbContext> options) : DbContext(options),
	IDatabaseContext<AuthenticationDbContext>
{
	public DbSet<CredentialRow> Credentials => Set<CredentialRow>();
	public DbSet<SessionRow> Sessions => Set<SessionRow>();

	static string IDatabaseContext<AuthenticationDbContext>.HistoryTable => "__ef_migrations_auth";

	static AuthenticationDbContext IDatabaseContext<AuthenticationDbContext>.Create(DbContextOptions<AuthenticationDbContext> options)
	{
		return new AuthenticationDbContext(options);
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.MapUsersReference(modelBuilder);
		ContextModel.ApplyModuleConfigurations<AuthenticationDbContext>(modelBuilder);
	}
}