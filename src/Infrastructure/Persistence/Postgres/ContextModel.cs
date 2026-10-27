using Basil.Infrastructure.Persistence.Scores;
using Basil.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Postgres;

/// <summary>The model pieces that every module context shares: its own configurations and the tables it references.</summary>
internal static class ContextModel
{
	/// <summary>Applies the configurations of the module the context belongs to.</summary>
	public static void ApplyModuleConfigurations<TContext>(ModelBuilder modelBuilder)
		where TContext : DbContext
	{
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(TContext).Assembly,
			type => type.Namespace == typeof(TContext).Namespace);
	}

	/// <summary>Maps the write tickets table, which the common module creates, without migrating it.</summary>
	public static void MapWriteTicketReference(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<WriteTicketRow>(row =>
		{
			row.ToTable("write_tickets", table => table.ExcludeFromMigrations());
			row.HasKey(ticket => ticket.Id);
			row.Property(ticket => ticket.Id).ValueGeneratedNever();
			row.HasIndex(ticket => ticket.CreatedAt);
		});
	}

	/// <summary>Maps the users table, which the users module creates, so foreign keys to it are generated without migrating it.</summary>
	public static void MapUserReference(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<UserRow>(row =>
		{
			row.ToTable("users", table => table.ExcludeFromMigrations());
			row.HasKey(user => user.Id);
		});
	}

	/// <summary>Maps the scores table, which the scores module creates, so foreign keys to it are generated without migrating it.</summary>
	public static void MapScoreReference(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<ScoreRow>(row =>
		{
			row.ToTable("scores", table => table.ExcludeFromMigrations());
			row.HasKey(score => score.Id);
		});
	}
}