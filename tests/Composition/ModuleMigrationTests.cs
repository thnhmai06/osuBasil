using System.Reflection;
using Basil.Infrastructure.Common.Postgres;
using Basil.Testing.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Basil.Composition.Tests;

/// <summary>Checks the module migrations preserve table ownership, migration order and foreign keys.</summary>
[Collection("Persistence")]
public sealed class ModuleMigrationTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly (string Module, string[] Tables)[] Modules =
	[
		("Common", ["write_tickets"]),
		("Users", ["users", "restrictions", "moderation_log", "relationships"]),
		("Auth", ["credentials", "logins"]),
		("Beatmaps", ["beatmapsets", "beatmaps"]),
		("Content", ["settings", "menu_banners"]),
		("Scores", ["scores", "user_stats"]),
		("Multiplayer", ["matches", "rounds", "match_events", "round_scores"]),
		("Chat", ["channels"])
	];

	[Fact]
	public void Module_migrations_create_only_tables_owned_by_their_context()
	{
		var assembly = typeof(DatabaseMigrator).Assembly;
		foreach (var (module, ownedTables) in Modules)
		{
			var moduleAssembly = module == "Common" ? assembly : Assembly.Load($"Basil.Infrastructure.{module}");
			var contextName = module == "Common"
				? "Basil.Infrastructure.Common.CommonDbContext"
				: $"Basil.Infrastructure.{module}.Persistence.{module}DbContext";
			var context = moduleAssembly.GetType(contextName);
			Assert.NotNull(context);

			var migrations = moduleAssembly.GetTypes()
				.Where(type => typeof(Migration).IsAssignableFrom(type) && !type.IsAbstract)
				.Where(type => type.GetCustomAttribute<DbContextAttribute>()?.ContextType == context)
				.ToArray();
			Assert.NotEmpty(migrations);

			var createdTables = migrations.SelectMany(type =>
					((Migration)Activator.CreateInstance(type, nonPublic: true)!).UpOperations
					.OfType<CreateTableOperation>())
				.Select(operation => operation.Name)
				.ToHashSet(StringComparer.Ordinal);

			Assert.Equal(ownedTables.Order(StringComparer.Ordinal), createdTables.Order(StringComparer.Ordinal));
		}
	}

	[Fact]
	public async Task Module_migrations_are_applied_in_the_documented_order()
	{
		var rows = await StoredRows.ReadAsync(ConnectionString, """
		                                                        SELECT module, transaction_id
		                                                        FROM (
		                                                        	SELECT 'common' AS module, xmin::text::bigint AS transaction_id FROM "__ef_migrations_common"
		                                                        	UNION ALL SELECT 'users', xmin::text::bigint FROM "__ef_migrations_users"
		                                                        	UNION ALL SELECT 'auth', xmin::text::bigint FROM "__ef_migrations_auth"
		                                                        	UNION ALL SELECT 'beatmaps', xmin::text::bigint FROM "__ef_migrations_beatmaps"
		                                                        	UNION ALL SELECT 'content', xmin::text::bigint FROM "__ef_migrations_content"
		                                                        	UNION ALL SELECT 'scores', xmin::text::bigint FROM "__ef_migrations_scores"
		                                                        	UNION ALL SELECT 'multiplayer', xmin::text::bigint FROM "__ef_migrations_multiplayer"
		                                                        	UNION ALL SELECT 'chat', xmin::text::bigint FROM "__ef_migrations_chat"
		                                                        ) AS migrations
		                                                        ORDER BY transaction_id
		                                                        """, null, TestContext.Current.CancellationToken);

		Assert.Equal(["common", "users", "auth", "beatmaps", "content", "scores", "multiplayer", "chat"],
			rows.Select(row => row.Text("module")));
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
			"fk_logins_users_user_id",
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
			"fk_user_stats_users_user_id"
		];

		Assert.Equal(expected, rows.Select(row => row.Text("conname")));
	}
}