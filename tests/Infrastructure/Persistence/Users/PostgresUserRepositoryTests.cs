using Basil.Application.Persistence.Common;
using Basil.Application.Persistence.Users;
using Basil.Domain.Permissions;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Users;

/// <summary>Contract tests of the storing of registered users.</summary>
/// <remarks>The migration stores BasilBot as account <see cref="BasilBot.Id" />, so every database starts with that one account.</remarks>
[Collection("Persistence")]
public sealed class PostgresUserRepositoryTests : DatabaseTests
{
	public PostgresUserRepositoryTests(PersistenceFixture container) : base(container) { }

	private IUserRepository Users => Get<IUserRepository>();

	[Fact]
	public async Task A_created_user_is_given_an_id_by_the_store()
	{
		var user = await Users.CreateAsync(new UserData { Name = "first_user" }, TestContext.Current.CancellationToken);

		Assert.Equal(BasilBot.Id + 1, user.Id);
	}

	[Fact]
	public async Task Created_users_are_given_increasing_ids()
	{
		var first = await Users.CreateAsync(new UserData { Name = "older_user" }, TestContext.Current.CancellationToken);
		var second = await Users.CreateAsync(new UserData { Name = "newer_user" }, TestContext.Current.CancellationToken);

		Assert.True(second.Id > first.Id);
	}

	[Fact]
	public async Task A_name_that_differs_only_in_case_is_the_name_of_another_account()
	{
		await Users.CreateAsync(new UserData { Name = "same_name" }, TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			Users.CreateAsync(new UserData { Name = "SAME_NAME" }, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_name_that_differs_only_in_spaces_is_the_name_of_another_account()
	{
		await Users.CreateAsync(new UserData { Name = "some_name" }, TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			Users.CreateAsync(new UserData { Name = "Some Name" }, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_name_another_account_holds_in_the_database_is_refused()
	{
		await using var other = new StorageComposition(ConnectionString);
		await other.Get<IUserRepository>().CreateAsync(new UserData { Name = "taken_elsewhere" }, TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			Users.CreateAsync(new UserData { Name = "taken_elsewhere" }, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task An_account_is_found_by_a_name_that_differs_only_in_case_and_spaces()
	{
		var user = await Users.CreateAsync(new UserData { Name = "some_name" }, TestContext.Current.CancellationToken);

		var found = await Users.GetByNameAsync("Some Name", TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(user.Id, found.Id);
	}

	[Fact]
	public async Task A_deleted_account_frees_its_name()
	{
		var user = await Users.CreateAsync(new UserData { Name = "freed_name" }, TestContext.Current.CancellationToken);
		await DeleteAsync(user);
		await WaitForDeletionAsync(user.Id);

		var second = await Users.CreateAsync(new UserData { Name = "freed_name" }, TestContext.Current.CancellationToken);

		Assert.NotEqual(user.Id, second.Id);
	}

	[Fact]
	public async Task A_name_is_taken_as_soon_as_its_account_is_deleted()
	{
		var user = await Users.CreateAsync(new UserData { Name = "taken_name" }, TestContext.Current.CancellationToken);
		user.Value.DeletedAt = DateTimeOffset.UtcNow;
		await Users.UpdateAsync(user, TestContext.Current.CancellationToken);

		var second = await Users.CreateAsync(new UserData { Name = "taken_name" }, TestContext.Current.CancellationToken);

		Assert.NotEqual(user.Id, second.Id);
	}

	[Fact]
	public async Task A_deleted_account_is_still_found_by_id()
	{
		var user = await Users.CreateAsync(new UserData { Name = "deleted_user" }, TestContext.Current.CancellationToken);
		await DeleteAsync(user);

		var found = await Users.GetAsync(user.Id, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(user.Id, found.Id);
		Assert.NotNull(found.Value.DeletedAt);
	}

	[Fact]
	public async Task A_deleted_account_is_not_found_by_name()
	{
		var user = await Users.CreateAsync(new UserData { Name = "hidden_name" }, TestContext.Current.CancellationToken);
		await DeleteAsync(user);
		await WaitForDeletionAsync(user.Id);

		Assert.Null(await Users.GetByNameAsync("hidden_name", TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Two_reads_of_one_account_return_the_same_instance()
	{
		var user = await Users.CreateAsync(new UserData { Name = "same_one" }, TestContext.Current.CancellationToken);

		var byId = await Users.GetAsync(user.Id, TestContext.Current.CancellationToken);
		var byName = await Users.GetByNameAsync("same_one", TestContext.Current.CancellationToken);

		Assert.NotNull(byId);
		Assert.NotNull(byName);
		Assert.Same(byId, byName);
	}

	[Fact]
	public async Task An_update_is_seen_at_once()
	{
		var user = await Users.CreateAsync(new UserData { Name = "updated_user", Country = Country.Jp }, TestContext.Current.CancellationToken);
		user.Value.Country = Country.De;

		await Users.UpdateAsync(user, TestContext.Current.CancellationToken);

		var found = await Users.GetAsync(user.Id, TestContext.Current.CancellationToken);
		Assert.Equal(Country.De, found!.Value.Country);
	}

	[Fact]
	public async Task An_update_is_stored()
	{
		var user = await Users.CreateAsync(new UserData { Name = "stored_user", Country = Country.Jp }, TestContext.Current.CancellationToken);
		user.Value.Country = Country.De;
		await Users.UpdateAsync(user, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select country from users where id = @id",
			new Dictionary<string, object?> { ["@id"] = user.Id },
			rows => rows.Count == 1 && rows[0].Number("country") == (int)Country.De,
			"the stored country of the account", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task A_deleted_account_is_never_restored()
	{
		var user = await Users.CreateAsync(new UserData { Name = "never_back" }, TestContext.Current.CancellationToken);
		user.Value.DeletedAt = DateTimeOffset.UtcNow;

		Assert.Throws<InvalidOperationException>(() => user.Value.DeletedAt = null);
	}

	[Fact]
	public async Task A_rename_to_a_free_name_gives_the_account_the_new_name()
	{
		var user = await Users.CreateAsync(new UserData { Name = "before_rename" }, TestContext.Current.CancellationToken);

		Assert.True(await Users.RenameAsync(user, "after_rename", TestContext.Current.CancellationToken));

		var renamed = await Users.GetByNameAsync("after_rename", TestContext.Current.CancellationToken);
		Assert.NotNull(renamed);
		Assert.Equal(user.Id, renamed.Id);
	}

	[Fact]
	public async Task A_rename_frees_the_old_name()
	{
		var user = await Users.CreateAsync(new UserData { Name = "before_rename" }, TestContext.Current.CancellationToken);
		await Users.RenameAsync(user, "after_rename", TestContext.Current.CancellationToken);

		Assert.Null(await Users.GetByNameAsync("before_rename", TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_rename_to_a_taken_name_leaves_the_account_alone()
	{
		await Users.CreateAsync(new UserData { Name = "taken_first" }, TestContext.Current.CancellationToken);
		var second = await Users.CreateAsync(new UserData { Name = "taken_second" }, TestContext.Current.CancellationToken);

		Assert.False(await Users.RenameAsync(second, "taken_first", TestContext.Current.CancellationToken));

		Assert.Equal("taken_second", second.Value.Name);
		Assert.NotNull(await Users.GetByNameAsync("taken_second", TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_rename_to_the_same_name_in_another_case_succeeds()
	{
		var user = await Users.CreateAsync(new UserData { Name = "case_user" }, TestContext.Current.CancellationToken);

		Assert.True(await Users.RenameAsync(user, "CASE_USER", TestContext.Current.CancellationToken));

		Assert.Equal("CASE_USER", user.Value.Name);
	}

	[Fact]
	public async Task A_rename_to_a_name_another_account_holds_in_the_database_is_refused()
	{
		await using var other = new StorageComposition(ConnectionString);
		await other.Get<IUserRepository>().CreateAsync(new UserData { Name = "taken_elsewhere" }, TestContext.Current.CancellationToken);
		var user = await Users.CreateAsync(new UserData { Name = "mover_user" }, TestContext.Current.CancellationToken);

		Assert.False(await Users.RenameAsync(user, "taken_elsewhere", TestContext.Current.CancellationToken));

		Assert.Equal("mover_user", user.Value.Name);
	}

	[Fact]
	public async Task A_listing_finds_accounts_by_a_part_of_their_name()
	{
		await Users.CreateAsync(new UserData { Name = "alpha_one" }, TestContext.Current.CancellationToken);
		await Users.CreateAsync(new UserData { Name = "beta_two" }, TestContext.Current.CancellationToken);

		var page = await Users.ListAsync(new UserQuery(Text: "alpha"), new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal("alpha_one", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task A_listing_finds_accounts_by_a_name_that_holds_an_underscore()
	{
		await Users.CreateAsync(new UserData { Name = "alpha_one" }, TestContext.Current.CancellationToken);
		await Users.CreateAsync(new UserData { Name = "beta_two" }, TestContext.Current.CancellationToken);

		var page = await Users.ListAsync(new UserQuery(Text: "alpha_one"), new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal("alpha_one", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task A_listing_ignores_the_case_and_the_spaces_of_the_text()
	{
		await Users.CreateAsync(new UserData { Name = "alpha_one" }, TestContext.Current.CancellationToken);
		await Users.CreateAsync(new UserData { Name = "beta_two" }, TestContext.Current.CancellationToken);

		var page = await Users.ListAsync(new UserQuery(Text: "ALPHA ONE"), new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal("alpha_one", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task A_listing_finds_an_account_by_its_id()
	{
		var user = await Users.CreateAsync(new UserData { Name = "id_user" }, TestContext.Current.CancellationToken);
		await Users.CreateAsync(new UserData { Name = "other_one" }, TestContext.Current.CancellationToken);

		var page = await Users.ListAsync(new UserQuery(Text: user.Id.ToString()), new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(user.Id, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task A_listing_finds_accounts_by_their_country()
	{
		await Users.CreateAsync(new UserData { Name = "from_jp", Country = Country.Jp }, TestContext.Current.CancellationToken);
		await Users.CreateAsync(new UserData { Name = "from_de", Country = Country.De }, TestContext.Current.CancellationToken);

		var page = await Users.ListAsync(new UserQuery(Countries: [Country.Jp]), new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(Country.Jp, Assert.Single(page.Items).Value.Country);
	}

	[Fact]
	public async Task A_listing_finds_accounts_by_the_permissions_they_hold()
	{
		await Users.CreateAsync(new UserData { Name = "plain_user", Permissions = Permissions.None }, TestContext.Current.CancellationToken);
		await Users.CreateAsync(new UserData
		{
			Name = "admin_user",
			Permissions = PermissionsGroups.Default | Permissions.ServerConfigure
		}, TestContext.Current.CancellationToken);

		var page = await Users.ListAsync(new UserQuery(Permissions: Permissions.ServerConfigure),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal("admin_user", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task A_listing_without_a_state_shows_deleted_and_existing_accounts()
	{
		await TwoAccountsOneDeletedAsync();

		var page = await Users.ListAsync(new UserQuery(), new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(3, page.Total);
	}

	[Fact]
	public async Task A_listing_of_existing_accounts_leaves_deleted_accounts_out()
	{
		await TwoAccountsOneDeletedAsync();

		var page = await Users.ListAsync(new UserQuery(Deleted: false), new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(2, page.Total);
	}

	[Fact]
	public async Task A_listing_of_deleted_accounts_shows_only_deleted_accounts()
	{
		await TwoAccountsOneDeletedAsync();

		var page = await Users.ListAsync(new UserQuery(Deleted: true), new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(1, page.Total);
		Assert.NotNull(page.Items[0].Value.DeletedAt);
	}

	[Fact]
	public async Task A_listing_returns_the_requested_part()
	{
		await Users.CreateAsync(new UserData { Name = "page_one" }, TestContext.Current.CancellationToken);
		await Users.CreateAsync(new UserData { Name = "page_two" }, TestContext.Current.CancellationToken);
		await Users.CreateAsync(new UserData { Name = "page_three" }, TestContext.Current.CancellationToken);

		var page = await Users.ListAsync(new UserQuery(), new PageRequest(1, 2), TestContext.Current.CancellationToken);

		Assert.Equal(4, page.Total);
		Assert.Equal(2, page.Items.Count);
	}

	[Fact]
	public async Task A_listing_returns_no_more_accounts_than_asked_for()
	{
		await Users.CreateAsync(new UserData { Name = "page_one" }, TestContext.Current.CancellationToken);
		await Users.CreateAsync(new UserData { Name = "page_two" }, TestContext.Current.CancellationToken);
		await Users.CreateAsync(new UserData { Name = "page_three" }, TestContext.Current.CancellationToken);

		var page = await Users.ListAsync(new UserQuery(), new PageRequest(0, 2), TestContext.Current.CancellationToken);

		Assert.Equal(4, page.Total);
		Assert.Equal(2, page.Items.Count);
	}

	[Fact]
	public async Task A_listing_is_ordered_by_id()
	{
		var first = await Users.CreateAsync(new UserData { Name = "order_one" }, TestContext.Current.CancellationToken);
		var second = await Users.CreateAsync(new UserData { Name = "order_two" }, TestContext.Current.CancellationToken);
		var third = await Users.CreateAsync(new UserData { Name = "order_three" }, TestContext.Current.CancellationToken);

		var page = await Users.ListAsync(new UserQuery(), new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(new[] { BasilBot.Id, first.Id, second.Id, third.Id }, page.Items.Select(account => account.Id));
	}

	/// <summary>Deletes an account and waits until the deletion is stored.</summary>
	private async Task DeleteAsync(UserRecord user)
	{
		user.Value.DeletedAt = DateTimeOffset.UtcNow;
		await Users.UpdateAsync(user, TestContext.Current.CancellationToken);
	}

	/// <summary>Waits until the deletion of an account is stored.</summary>
	/// <param name="id">The id of the deleted account.</param>
	private async Task WaitForDeletionAsync(int id)
	{
		await StoredRows.WaitForAsync(ConnectionString,
			"select id from users where id = @id and deleted_at is not null",
			new Dictionary<string, object?> { ["@id"] = id }, rows => rows.Count == 1,
			$"the deletion of the account {id}", TestContext.Current.CancellationToken);
	}

	/// <summary>Creates two accounts and deletes the second one.</summary>
	private async Task TwoAccountsOneDeletedAsync()
	{
		await Users.CreateAsync(new UserData { Name = "living_user" }, TestContext.Current.CancellationToken);
		var deleted = await Users.CreateAsync(new UserData { Name = "dead_user" }, TestContext.Current.CancellationToken);
		await DeleteAsync(deleted);
		await WaitForDeletionAsync(deleted.Id);
	}
}
