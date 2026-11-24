using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Shared.Persistence;

public static class ContextModel
{
	public const string UsersTable = "UsersPrincipal";

	public const string ScoresTable = "ScoresPrincipal";

	public static void ApplyModuleConfigurations<TContext>(ModelBuilder modelBuilder)
		where TContext : DbContext
	{
		// Only the context's own namespace is read: other types of the module (the osu! adapters) may name assemblies
		// that are loaded later, or never, and must not be touched while the model is built.
		var apply = typeof(ModelBuilder).GetMethods().Single(method =>
			method.Name == nameof(ModelBuilder.ApplyConfiguration) &&
			method.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>));
		foreach (var type in typeof(TContext).Assembly.GetTypes()
			         .Where(type => type.Namespace == typeof(TContext).Namespace && type is
				         { IsAbstract: false, IsGenericTypeDefinition: false }))
		foreach (var configuration in type.GetInterfaces().Where(face =>
			         face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)))
			apply.MakeGenericMethod(configuration.GetGenericArguments()[0])
				.Invoke(modelBuilder, [Activator.CreateInstance(type)]);
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