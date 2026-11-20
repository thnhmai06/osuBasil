using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Shared.Persistence;

public static class ContextModel
{
	public const string UsersTable = "UsersPrincipal";

	public const string ScoresTable = "ScoresPrincipal";

	public static void ApplyModuleConfigurations<TContext>(ModelBuilder modelBuilder)
		where TContext : DbContext
	{
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(TContext).Assembly,
			type => type.Namespace == typeof(TContext).Namespace);
	}

	public static void MapUsersReference(ModelBuilder modelBuilder)
	{
		MapReference(modelBuilder, UsersTable, "users");
	}

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
}