using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Common.Persistence;

/// <summary>The model pieces that every module context shares: its own configurations and the tables it references.</summary>
public static class ContextModel
{
	/// <summary>Applies the configurations of the module the context belongs to.</summary>
	/// <typeparam name="TContext">The context whose configuration is applied.</typeparam>
	/// <param name="modelBuilder">The model being built.</param>
	public static void ApplyModuleConfigurations<TContext>(ModelBuilder modelBuilder)
		where TContext : DbContext
	{
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(TContext).Assembly,
			type => type.Namespace == typeof(TContext).Namespace);
	}

	/// <summary>The name of the entity type that stands for the <c>users</c> table, which the users module owns.</summary>
	public const string UsersTable = "UsersPrincipal";

	/// <summary>The name of the entity type that stands for the <c>scores</c> table, which the scores module owns.</summary>
	public const string ScoresTable = "ScoresPrincipal";

	/// <summary>Maps the users table, which the users module creates, without migrating it.</summary>
	/// <param name="modelBuilder">The model being built.</param>
	public static void MapUsersReference(ModelBuilder modelBuilder)
	{
		MapReference(modelBuilder, UsersTable, "users");
	}

	/// <summary>Maps the scores table, which the scores module creates, without migrating it.</summary>
	/// <param name="modelBuilder">The model being built.</param>
	public static void MapScoresReference(ModelBuilder modelBuilder)
	{
		MapReference(modelBuilder, ScoresTable, "scores");
	}

	private static void MapReference(ModelBuilder modelBuilder, string entityName, string table)
	{
		modelBuilder.SharedTypeEntity<Dictionary<string, object>>(entityName, entity =>
		{
			entity.ToTable(table, builder => builder.ExcludeFromMigrations());
			entity.Property<int>("Id");
			entity.HasKey("Id");
		});
	}

	/// <summary>Maps the write tickets table, which the common module creates, without migrating it.</summary>
	/// <param name="modelBuilder">The model being built.</param>
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
}