using Basil.Application.Persistence.Users;
using Basil.Domain.Users;
using Basil.Common.Infrastructure.Postgres;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Postgres;

/// <summary>Contract tests of the migrator that brings the database up to date.</summary>
[Collection("Persistence")]
public sealed class DatabaseMigratorTests(PersistenceFixture container) : DatabaseTests(container)
{
	[Fact]
	public async Task Migrating_a_migrated_database_applies_no_further_migration()
	{
		var before = await AppliedMigrationsAsync();

		await Get<DatabaseMigrator>().MigrateAsync(TestContext.Current.CancellationToken);

		var after = await AppliedMigrationsAsync();
		Assert.Equal(before, after);
	}

	[Fact]
	public async Task Migrating_a_migrated_database_keeps_the_data()
	{
		var user = await Get<IUserRepository>().CreateAsync(new UserData { Name = "kept_user" },
			TestContext.Current.CancellationToken);

		await Get<DatabaseMigrator>().MigrateAsync(TestContext.Current.CancellationToken);

		var found = await Get<IUserRepository>().GetAsync(user.Id, TestContext.Current.CancellationToken);
		Assert.NotNull(found);
		Assert.Equal("kept_user", found.Value.Name);
	}

	/// <summary>Reads the migrations the database has been given.</summary>
	/// <returns>The migrations, in the order the database holds them.</returns>
	private async Task<IReadOnlyList<string>> AppliedMigrationsAsync()
	{
		var rows = await StoredRows.ReadAsync(ConnectionString, """
		                                                        SELECT migration_id, product_version FROM "__ef_migrations_common"
		                                                        UNION ALL SELECT migration_id, product_version FROM "__ef_migrations_users"
		                                                        UNION ALL SELECT migration_id, product_version FROM "__ef_migrations_auth"
		                                                        UNION ALL SELECT migration_id, product_version FROM "__ef_migrations_beatmaps"
		                                                        UNION ALL SELECT migration_id, product_version FROM "__ef_migrations_content"
		                                                        UNION ALL SELECT migration_id, product_version FROM "__ef_migrations_scores"
		                                                        UNION ALL SELECT migration_id, product_version FROM "__ef_migrations_multiplayer"
		                                                        UNION ALL SELECT migration_id, product_version FROM "__ef_migrations_chat"
		                                                        ORDER BY migration_id
		                                                        """, null, TestContext.Current.CancellationToken);
		return rows.Select(row => string.Join("|", row.Values.Values.Select(value => value?.ToString()))).ToList();
	}
}