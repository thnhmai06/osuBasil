using System.Reflection;
using Basil.Infrastructure.Persistence.Providers;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Persistence;
using Basil.Testing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Options;

namespace Basil.Tests;

/// <summary>Checks the module migrations preserve table ownership, migration order and foreign keys.</summary>
[Collection("Persistence")]
public sealed class ModuleMigrationTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly (string Module, string[] Tables)[] Modules =
	[
		("Users", ["users", "moderation_log", "relationships", "user_id_counters"]),
		("Authentication", ["credentials", "sessions", "connections", "connection_id_counters"]),
		("Beatmaps", ["beatmapsets", "beatmaps", "beatmap_id_counters"]),
		("Content", ["settings", "menu_banners"]),
		// The settings table is adopted from Content, which still creates it in its first migration.
		("Shared", []),
		("Scores", ["scores", "user_stats", "score_id_counters"]),
		("Multiplayer", ["matches", "rounds", "match_events", "round_scores", "multiplayer_id_counters"]),
		("Chat", ["channels"])
	];

	[Fact]
	public void ModuleMigrations_OwnTablesOnly()
	{
		var migrationsAssembly = typeof(PostgresDatabaseProvider).Assembly;
		foreach (var (module, ownedTables) in Modules)
		{
			var context = Context(module);
			var migrations = migrationsAssembly.GetTypes()
				.Where(type => typeof(Migration).IsAssignableFrom(type) && !type.IsAbstract)
				.Where(type => type.GetCustomAttribute<DbContextAttribute>()?.ContextType == context)
				.ToArray();
			Assert.NotEmpty(migrations);

			var createdTables = migrations.SelectMany(type =>
					((Migration)Activator.CreateInstance(type, true)!).UpOperations
					.OfType<CreateTableOperation>())
				.Select(operation => operation.Name)
				.ToHashSet(StringComparer.Ordinal);

			Assert.Equal(ownedTables.Order(StringComparer.Ordinal), createdTables.Order(StringComparer.Ordinal));
		}
	}

	[Fact]
	public void EveryModelChange_Migrated()
	{
		var create = typeof(Database).GetMethod(nameof(Database.CreateConfirmedContext))!;
		var database = Get<Database>();
		foreach (var (module, _) in Modules)
		{
			using var context = (DbContext)create.MakeGenericMethod(Context(module)).Invoke(database, null)!;

			Assert.False(context.Database.HasPendingModelChanges(), $"{module} has model changes without a migration.");
		}
	}

	[Fact]
	public async Task BothEngines_SameSchema()
	{
		var create = typeof(Database).GetMethod(nameof(Database.CreateConfirmedContext))!;
		foreach (var (module, _) in Modules)
		{
			var postgres = await Schema(create, module, "PostgreSQL", "Host=localhost;Database=schema");
			var mysql = await Schema(create, module, "MySql", "Server=localhost;Database=schema");

			Assert.Equal(postgres, mysql);
		}
	}

	private static async Task<string[]> Schema(MethodInfo create, string module, string provider,
		string connectionString)
	{
		var options = new DatabaseOptions { WriteConnectionString = connectionString, WriteProvider = provider };
		await using var database = new Database(Infrastructure.Persistence.DependencyInjection.Providers(options),
			Options.Create(options));
		using var context = (DbContext)create.MakeGenericMethod(Context(module)).Invoke(database, null)!;
		return context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables
			.SelectMany(table => table.Columns.Select(column => $"{table.Name}.{column.Name}"))
			.Order(StringComparer.Ordinal).ToArray();
	}

	private static Type Context(string module)
	{
		var type = Assembly.Load($"Basil.Infrastructure.{module}").GetTypes()
			.SingleOrDefault(candidate => candidate.Name == $"{module}DbContext");
		Assert.NotNull(type);
		return type;
	}

	[Fact]
	public async Task ModuleMigrations_DocumentedOrder()
	{
		// A module with several migrations has several rows; the order of the modules is what is pinned.
		var rows = await StoredRows.ReadAsync(ConnectionString, SchemaQueries.MigrationOrder, null,
			TestContext.Current.CancellationToken);

		Assert.Equal(["users", "auth", "beatmaps", "content", "shared", "scores", "multiplayer", "chat"],
			rows.Select(row => row.Text("module")).Distinct());
	}

	[Fact]
	public async Task Migrating_CreatesForeignKeys()
	{
		var rows = await StoredRows.ReadAsync(ConnectionString, SchemaQueries.ForeignKeys, null,
			TestContext.Current.CancellationToken);

		string[] expected =
		[
			"fk_beatmaps_beatmapsets_beatmapset_id",
			"fk_connections_sessions_session_id",
			"fk_credentials_users_user_id",
			"fk_match_events_matches_match_id",
			"fk_match_events_users_actor_id",
			"fk_match_events_users_target_id",
			"fk_matches_users_creator_id",
			"fk_moderation_log_users_by_id",
			"fk_moderation_log_users_target_id",
			"fk_relationships_users_actor_id",
			"fk_relationships_users_target_id",
			"fk_round_scores_scores_score_id",
			"fk_rounds_matches_match_id",
			"fk_scores_users_user_id",
			"fk_sessions_users_user_id",
			"fk_user_stats_users_user_id"
		];

		Assert.Equal(expected, rows.Select(row => row.Text("conname")));
	}
}