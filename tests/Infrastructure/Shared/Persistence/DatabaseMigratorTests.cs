using Basil.Application.Users.Persistence;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Shared.Tests.Persistence;

[Collection("Persistence")]
public sealed class DatabaseMigratorTests(PersistenceFixture container) : DatabaseTests(container)
{
	[Fact]
	public async Task MigratedDatabase_NoFurtherMigration()
	{
		var before = await AppliedMigrationsAsync();

		await Get<DatabaseMigrator>().MigrateAsync(TestContext.Current.CancellationToken);

		var after = await AppliedMigrationsAsync();
		Assert.Equal(before, after);
	}

	[Fact]
	public async Task MigratedDatabase_KeepsData()
	{
		var user = await Get<IUserRepository>().CreateAsync(new UserData { Name = "kept_user" });

		await Get<DatabaseMigrator>().MigrateAsync(TestContext.Current.CancellationToken);

		var found = await Get<IUserRepository>().GetAsync(user.Id, TestContext.Current.CancellationToken);
		Assert.NotNull(found);
		Assert.Equal("kept_user", found.Value.Name);
	}

	private async Task<IReadOnlyList<string>> AppliedMigrationsAsync()
	{
		var rows = await StoredRows.ReadAsync(ConnectionString, """
		                                                        SELECT migration_id, product_version FROM __ef_migrations_users
		                                                        UNION ALL SELECT migration_id, product_version FROM __ef_migrations_auth
		                                                        UNION ALL SELECT migration_id, product_version FROM __ef_migrations_beatmaps
		                                                        UNION ALL SELECT migration_id, product_version FROM __ef_migrations_content
		                                                        UNION ALL SELECT migration_id, product_version FROM __ef_migrations_shared
		                                                        UNION ALL SELECT migration_id, product_version FROM __ef_migrations_scores
		                                                        UNION ALL SELECT migration_id, product_version FROM __ef_migrations_multiplayer
		                                                        UNION ALL SELECT migration_id, product_version FROM __ef_migrations_chat
		                                                        ORDER BY migration_id
		                                                        """, null, TestContext.Current.CancellationToken);
		return rows.Select(row => string.Join("|", row.Values.Values.Select(value => value?.ToString()))).ToList();
	}
}