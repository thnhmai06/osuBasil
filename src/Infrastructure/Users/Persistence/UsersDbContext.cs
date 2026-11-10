using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Users.Persistence;

internal sealed class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContext(options),
	IDatabaseContext<UsersDbContext>
{
	/// <summary>The stored accounts.</summary>
	public DbSet<UserRow> Users => Set<UserRow>();
	/// <summary>The stored moderation actions.</summary>
	public DbSet<ModerationLogRow> ModerationLog => Set<ModerationLogRow>();
	/// <summary>The stored relationships.</summary>
	public DbSet<RelationshipRow> Relationships => Set<RelationshipRow>();

	static string IDatabaseContext<UsersDbContext>.HistoryTable => "__ef_migrations_users";

	static UsersDbContext IDatabaseContext<UsersDbContext>.Create(DbContextOptions<UsersDbContext> options)
	{
		return new UsersDbContext(options);
	}

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.ApplyModuleConfigurations<UsersDbContext>(modelBuilder);
		IdCounters.Map(modelBuilder, "user_id_counters", ("user", 2), ("moderation_log", 1));
	}
}