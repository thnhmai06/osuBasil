using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Authentication.Persistence;

internal sealed class AuthenticationDbContext(DbContextOptions<AuthenticationDbContext> options) : DbContext(options),
	IDatabaseContext<AuthenticationDbContext>
{
	/// <summary>The stored credentials.</summary>
	public DbSet<CredentialRow> Credentials => Set<CredentialRow>();
	/// <summary>The stored sessions.</summary>
	public DbSet<SessionRow> Sessions => Set<SessionRow>();
	/// <summary>The stored connections.</summary>
	public DbSet<ConnectionRow> Connections => Set<ConnectionRow>();

	static string IDatabaseContext<AuthenticationDbContext>.HistoryTable => "__ef_migrations_auth";

	static AuthenticationDbContext IDatabaseContext<AuthenticationDbContext>.Create(
		DbContextOptions<AuthenticationDbContext> options)
	{
		return new AuthenticationDbContext(options);
	}

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		IdCounters.Map(modelBuilder, "connection_id_counters", ("connection", 1));
		ContextModel.MapUsersReference(modelBuilder);
		ContextModel.ApplyModuleConfigurations<AuthenticationDbContext>(modelBuilder);
	}
}