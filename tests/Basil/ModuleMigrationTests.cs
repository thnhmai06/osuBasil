using System.Reflection;
using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Persistence;
using Basil.Testing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Basil.Tests;

/// <summary>Checks the module migrations preserve table ownership, migration order and foreign keys.</summary>
[Collection("Persistence")]
public sealed class ModuleMigrationTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly (string Module, string[] Tables)[] Modules =
	[
		("Users", ["users", "restrictions", "moderation_log", "relationships"]),
		("Authentication", ["credentials", "sessions"]),
		("Beatmaps", ["beatmapsets", "beatmaps"]),
		("Content", ["settings", "menu_banners"]),
		("Scores", ["scores", "user_stats"]),
		("Multiplayer", ["matches", "rounds", "match_events", "round_scores"]),
		("Chat", ["channels"])
	];

	[Fact]
	public void Module_migrations_create_only_tables_owned_by_their_context()
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
	public void Every_module_context_has_no_model_change_the_migrations_miss()
	{
		var create = typeof(Database).GetMethod(nameof(Database.CreateConfirmedContext))!;
		var database = Get<Database>();
		foreach (var (module, _) in Modules)
		{
			using var context = (DbContext)create.MakeGenericMethod(Context(module)).Invoke(database, null)!;

			Assert.False(context.Database.HasPendingModelChanges(), $"{module} has model changes without a migration.");
		}
	}

	private static Type Context(string module)
	{
		var type = Assembly.Load($"Basil.Infrastructure.{module}").GetTypes()
			.SingleOrDefault(candidate => candidate.Name == $"{module}DbContext");
		Assert.NotNull(type);
		return type;
	}

	[Fact]
	public async Task Module_migrations_are_applied_in_the_documented_order()
	{
		// A module with several migrations has several rows; the order of the modules is what is pinned.
		var rows = await StoredRows.ReadAsync(ConnectionString, """
		                                                        SELECT module, transaction_id
		                                                        FROM (
		                                                        	SELECT 'users' AS module, xmin::text::bigint AS transaction_id FROM "__ef_migrations_users"
		                                                        	UNION ALL SELECT 'auth', xmin::text::bigint FROM "__ef_migrations_auth"
		                                                        	UNION ALL SELECT 'beatmaps', xmin::text::bigint FROM "__ef_migrations_beatmaps"
		                                                        	UNION ALL SELECT 'content', xmin::text::bigint FROM "__ef_migrations_content"
		                                                        	UNION ALL SELECT 'scores', xmin::text::bigint FROM "__ef_migrations_scores"
		                                                        	UNION ALL SELECT 'multiplayer', xmin::text::bigint FROM "__ef_migrations_multiplayer"
		                                                        	UNION ALL SELECT 'chat', xmin::text::bigint FROM "__ef_migrations_chat"
		                                                        ) AS migrations
		                                                        ORDER BY transaction_id
		                                                        """, null, TestContext.Current.CancellationToken);

		Assert.Equal(["users", "auth", "beatmaps", "content", "scores", "multiplayer", "chat"],
			rows.Select(row => row.Text("module")).Distinct());
	}

	[Fact]
	public async Task Migrating_creates_every_foreign_key()
	{
		var rows = await StoredRows.ReadAsync(ConnectionString,
			"SELECT conname FROM pg_constraint WHERE contype = 'f' ORDER BY conname", null,
			TestContext.Current.CancellationToken);

		string[] expected =
		[
			"fk_beatmaps_beatmapsets_beatmapset_id",
			"fk_credentials_users_user_id",
			"fk_match_events_matches_match_id",
			"fk_match_events_users_actor_id",
			"fk_match_events_users_target_id",
			"fk_matches_users_creator_id",
			"fk_moderation_log_users_by_id",
			"fk_moderation_log_users_target_id",
			"fk_relationships_users_actor_id",
			"fk_relationships_users_target_id",
			"fk_restrictions_users_user_id",
			"fk_round_scores_scores_score_id",
			"fk_rounds_matches_match_id",
			"fk_scores_users_user_id",
			"fk_sessions_users_user_id",
			"fk_user_stats_users_user_id"
		];

		Assert.Equal(expected, rows.Select(row => row.Text("conname")));
	}
}