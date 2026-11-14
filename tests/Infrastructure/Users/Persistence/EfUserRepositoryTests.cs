using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Users.Tests.Persistence;

/// <summary>Contract tests of the storing of registered users.</summary>
/// <remarks>
///     A new database holds no account. BasilBot is stored under <see cref="BasilBot.Id" /> by the server at start-up,
///     so the first account the database numbers is <c>BasilBot.Id + 1</c>.
/// </remarks>
[Collection("Persistence")]
public sealed class EfUserRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private IUserRepository Users => Get<IUserRepository>();

	[Fact]
	public async Task CreatedUser_GetsStoreId()
	{
		var user = await Users.CreateAsync(new UserData { Name = "first_user" });

		Assert.Equal(BasilBot.Id + 1, user.Id);
	}

	[Fact]
	public async Task CreatedUsers_GetIncreasingIds()
	{
		var first = await Users.CreateAsync(new UserData { Name = "older_user" });
		var second =
			await Users.CreateAsync(new UserData { Name = "newer_user" });

		Assert.True(second.Id > first.Id);
	}

	[Fact]
	public async Task CaseDifference_MeansOtherAccount()
	{
		await Users.CreateAsync(new UserData { Name = "same_name" });

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			Users.CreateAsync(new UserData { Name = "SAME_NAME" }));
	}

	[Fact]
	public async Task SpaceDifference_MeansOtherAccount()
	{
		await Users.CreateAsync(new UserData { Name = "some_name" });

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			Users.CreateAsync(new UserData { Name = "Some Name" }));
	}

	[Fact]
	public async Task TakenName_Refused()
	{
		await using var other = new StorageComposition(ConnectionString);
		await other.Get<IUserRepository>().CreateAsync(new UserData { Name = "taken_elsewhere" });

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			Users.CreateAsync(new UserData { Name = "taken_elsewhere" }));
	}

	[Fact]
	public async Task CaseSpaceDifference_AccountFound()
	{
		var user = await Users.CreateAsync(new UserData { Name = "some_name" });

		var found = await Users.GetByNameAsync("Some Name", TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(user.Id, found.Id);
	}

	[Fact]
	public async Task DeletedAccount_FreesName()
	{
		var user = await Users.CreateAsync(new UserData { Name = "freed_name" });
		await DeleteAsync(user);

		var second =
			await Users.CreateAsync(new UserData { Name = "freed_name" });

		Assert.NotEqual(user.Id, second.Id);
	}

	[Fact]
	public async Task DeletedName_ImmediatelyTaken()
	{
		var user = await Users.CreateAsync(new UserData { Name = "taken_name" });
		user.Value.DeletedAt = DateTimeOffset.UtcNow;
		await Users.UpdateAsync(user);

		var second =
			await Users.CreateAsync(new UserData { Name = "taken_name" });

		Assert.NotEqual(user.Id, second.Id);
	}

	[Fact]
	public async Task DeletedAccount_FoundById()
	{
		var user = await Users.CreateAsync(new UserData { Name = "deleted_user" });
		await DeleteAsync(user);

		var found = await Users.GetAsync(user.Id, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(user.Id, found.Id);
		Assert.NotNull(found.Value.DeletedAt);
	}

	[Fact]
	public async Task DeletedAccount_NameLookupFails()
	{
		var user = await Users.CreateAsync(new UserData { Name = "hidden_name" });
		await DeleteAsync(user);

		Assert.Null(await Users.GetByNameAsync("hidden_name", TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ReplacedAccount_NewDataKept()
	{
		var replaced = await Users.ReplaceAsync(BasilBot.Id,
			new UserData { Name = "renamed_bot", Privileges = UserPrivileges.None });

		var found = await Users.GetAsync(BasilBot.Id, TestContext.Current.CancellationToken);

		Assert.Equal(BasilBot.Id, replaced.Id);
		Assert.Equal("renamed_bot", found!.Value.Name);
		Assert.Equal(BasilBot.Id,
			(await Users.GetByNameAsync("renamed_bot", TestContext.Current.CancellationToken))!.Id);
	}

	[Fact]
	public async Task ReplacedAccount_Stored()
	{
		await Users.ReplaceAsync(BasilBot.Id, new UserData { Name = "stored_bot", Privileges = UserPrivileges.None });

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select name, privileges from users where id = @id",
			new Dictionary<string, object?> { ["@id"] = BasilBot.Id },
			rows => rows.Count == 1 && rows[0].Text("name") == "stored_bot", "the replaced account",
			TestContext.Current.CancellationToken);
		Assert.Equal(0, rows[0].Number("privileges"));
	}

	[Fact]
	public async Task DeletedAccount_RestoredByReplace()
	{
		var user = await Users.CreateAsync(new UserData { Name = "was_deleted" });
		await DeleteAsync(user);

		var restored = await Users.ReplaceAsync(user.Id, new UserData { Name = "restored_user" });

		Assert.Null(restored.Value.DeletedAt);
		Assert.Equal(user.Id,
			(await Users.GetByNameAsync("restored_user", TestContext.Current.CancellationToken))!.Id);
		await StoredRows.WaitForAsync(ConnectionString, "select id from users where id = @id and deleted_at is null",
			new Dictionary<string, object?> { ["@id"] = user.Id }, rows => rows.Count == 1,
			"the restored account", TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task UnknownId_CreatedByReplace()
	{
		const int id = 5000;

		var created = await Users.ReplaceAsync(id, new UserData { Name = "chosen_id" });

		Assert.Equal(id, created.Id);
		Assert.Equal(id, (await Users.GetAsync(id, TestContext.Current.CancellationToken))!.Id);
		await StoredRows.WaitForAsync(ConnectionString, "select id from users where id = @id",
			new Dictionary<string, object?> { ["@id"] = id }, rows => rows.Count == 1,
			"the created account", TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task ReplacementTakenName_Refused()
	{
		await Users.CreateAsync(new UserData { Name = "name_in_use" });

		await Assert.ThrowsAsync<AlreadyExistsException>(() => Users.ReplaceAsync(BasilBot.Id,
			new UserData { Name = "Name In Use" }));
	}

	[Fact]
	public async Task Replace_KeepsIssuedIds()
	{
		await Users.ReplaceAsync(BasilBot.Id, new UserData { Name = "bot_again" });

		var user = await Users.CreateAsync(new UserData { Name = "after_replace" });

		Assert.Equal(BasilBot.Id + 1, user.Id);
	}

	[Fact]
	public async Task RepeatedRead_SameAccount()
	{
		var user = await Users.CreateAsync(new UserData { Name = "same_one" });

		var byId = await Users.GetAsync(user.Id, TestContext.Current.CancellationToken);
		var byName = await Users.GetByNameAsync("same_one", TestContext.Current.CancellationToken);

		Assert.NotNull(byId);
		Assert.NotNull(byName);
		Assert.Equal(byId.Id, byName.Id);
	}

	[Fact]
	public async Task Update_SeenAtOnce()
	{
		var user = await Users.CreateAsync(new UserData { Name = "updated_user", Country = Country.Jp });
		user.Value.Country = Country.De;

		await Users.UpdateAsync(user);

		var found = await Users.GetAsync(user.Id, TestContext.Current.CancellationToken);
		Assert.Equal(Country.De, found!.Value.Country);
	}

	[Fact]
	public async Task Update_Stored()
	{
		var user = await Users.CreateAsync(new UserData { Name = "stored_user", Country = Country.Jp });
		user.Value.Country = Country.De;
		await Users.UpdateAsync(user);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select country from users where id = @id",
			new Dictionary<string, object?> { ["@id"] = user.Id },
			rows => rows.Count == 1 && rows[0].Number("country") == (int)Country.De,
			"the stored country of the account", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task DeletedAccount_NeverRestored()
	{
		var user = await Users.CreateAsync(new UserData { Name = "never_back" });
		user.Value.DeletedAt = DateTimeOffset.UtcNow;

		Assert.Throws<InvalidOperationException>(() => user.Value.DeletedAt = null);
	}

	[Fact]
	public async Task DiscardedAccount_GoneNameFree()
	{
		var user = await Users.CreateAsync(new UserData { Name = "discarded_user" });

		await Users.DiscardAsync(user);

		Assert.Null(await Users.GetAsync(user.Id, TestContext.Current.CancellationToken));
		Assert.Null(await Users.GetByNameAsync("discarded_user", TestContext.Current.CancellationToken));
		await Users.CreateAsync(new UserData { Name = "discarded_user" });
	}

	[Fact]
	public async Task StaleUpdate_DoesNotRestore()
	{
		var user = await Users.CreateAsync(new UserData { Name = "stale_record" });
		await DeleteAsync(user);
		var stale = new UserRecord
			{ Id = user.Id, Value = new UserData { Name = "stale_record", Country = Country.De } };

		await Users.UpdateAsync(stale);

		var found = await Users.GetAsync(user.Id, TestContext.Current.CancellationToken);
		Assert.NotNull(found!.Value.DeletedAt);
		Assert.Equal(Country.De, found.Value.Country);
	}

	[Fact]
	public async Task FreeNameRename_Applied()
	{
		var user = await Users.CreateAsync(new UserData { Name = "before_rename" });

		Assert.True(await Users.RenameAsync(user, "after_rename"));

		var renamed = await Users.GetByNameAsync("after_rename", TestContext.Current.CancellationToken);
		Assert.NotNull(renamed);
		Assert.Equal(user.Id, renamed.Id);
	}

	[Fact]
	public async Task Rename_FreesOldName()
	{
		var user = await Users.CreateAsync(new UserData { Name = "before_rename" });
		await Users.RenameAsync(user, "after_rename");

		Assert.Null(await Users.GetByNameAsync("before_rename", TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task TakenRename_AccountUnchanged()
	{
		await Users.CreateAsync(new UserData { Name = "taken_first" });
		var second =
			await Users.CreateAsync(new UserData { Name = "taken_second" });

		Assert.False(await Users.RenameAsync(second, "taken_first"));

		Assert.Equal("taken_second", second.Value.Name);
		Assert.NotNull(await Users.GetByNameAsync("taken_second", TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CaseOnlyRename_Allowed()
	{
		var user = await Users.CreateAsync(new UserData { Name = "case_user" });

		Assert.True(await Users.RenameAsync(user, "CASE_USER"));

		Assert.Equal("CASE_USER", user.Value.Name);
	}

	[Fact]
	public async Task StoredNameRename_Refused()
	{
		await using var other = new StorageComposition(ConnectionString);
		await other.Get<IUserRepository>().CreateAsync(new UserData { Name = "taken_elsewhere" });
		var user = await Users.CreateAsync(new UserData { Name = "mover_user" });

		Assert.False(await Users.RenameAsync(user, "taken_elsewhere"));

		Assert.Equal("mover_user", user.Value.Name);
	}

	[Fact]
	public async Task Listing_NamePartFound()
	{
		await Users.CreateAsync(new UserData { Name = "alpha_one" });
		await Users.CreateAsync(new UserData { Name = "beta_two" });

		var page = await Users.ListAsync(new UserQuery("alpha"), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal("alpha_one", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task Listing_FindsUnderscoreName()
	{
		await Users.CreateAsync(new UserData { Name = "alpha_one" });
		await Users.CreateAsync(new UserData { Name = "beta_two" });

		var page = await Users.ListAsync(new UserQuery("alpha_one"), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal("alpha_one", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task Listing_CaseSpaceInsensitive()
	{
		await Users.CreateAsync(new UserData { Name = "alpha_one" });
		await Users.CreateAsync(new UserData { Name = "beta_two" });

		var page = await Users.ListAsync(new UserQuery("ALPHA ONE"), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal("alpha_one", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task Listing_FindsById()
	{
		var user = await Users.CreateAsync(new UserData { Name = "id_user" });
		await Users.CreateAsync(new UserData { Name = "other_one" });

		var page = await Users.ListAsync(new UserQuery(user.Id.ToString()), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(user.Id, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task Listing_FindsByCountry()
	{
		await Users.CreateAsync(new UserData { Name = "from_jp", Country = Country.Jp });
		await Users.CreateAsync(new UserData { Name = "from_de", Country = Country.De });

		var page = await Users.ListAsync(new UserQuery(Countries: [Country.Jp]), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(Country.Jp, Assert.Single(page.Items).Value.Country);
	}

	[Fact]
	public async Task Listing_FindsByPrivileges()
	{
		await Users.CreateAsync(new UserData { Name = "plain_user", Privileges = UserPrivileges.None });
		await Users.CreateAsync(new UserData
		{
			Name = "admin_user",
			Privileges = UserPrivilegesExtensions.Default | UserPrivileges.Developer
		});

		var page = await Users.ListAsync(new UserQuery(Privileges: UserPrivileges.Developer),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal("admin_user", Assert.Single(page.Items).Value.Name);
	}

	[Fact]
	public async Task NoStateListing_ShowsBoth()
	{
		await TwoAccountsOneDeletedAsync();

		var page = await Users.ListAsync(new UserQuery(), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(2, page.Items.Count);
	}

	[Fact]
	public async Task ExistingListing_SkipsDeleted()
	{
		await TwoAccountsOneDeletedAsync();

		var page = await Users.ListAsync(new UserQuery(Deleted: false), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Single(page.Items);
	}

	[Fact]
	public async Task DeletedListing_OnlyDeleted()
	{
		await TwoAccountsOneDeletedAsync();

		var page = await Users.ListAsync(new UserQuery(Deleted: true), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Single(page.Items);
		Assert.NotNull(page.Items[0].Value.DeletedAt);
	}

	[Fact]
	public async Task Listing_ReturnsRequestedPage()
	{
		await Users.CreateAsync(new UserData { Name = "page_one" });
		await Users.CreateAsync(new UserData { Name = "page_two" });
		await Users.CreateAsync(new UserData { Name = "page_three" });

		var page = await Users.ListAsync(new UserQuery(), new PageRequest(1, 2), TestContext.Current.CancellationToken);

		Assert.False(page.HasNext);
		Assert.Equal(2, page.Items.Count);
	}

	[Fact]
	public async Task Listing_RespectsPageSize()
	{
		await Users.CreateAsync(new UserData { Name = "page_one" });
		await Users.CreateAsync(new UserData { Name = "page_two" });
		await Users.CreateAsync(new UserData { Name = "page_three" });

		var page = await Users.ListAsync(new UserQuery(), new PageRequest(0, 2), TestContext.Current.CancellationToken);

		Assert.True(page.HasNext);
		Assert.Equal(2, page.Items.Count);
	}

	[Fact]
	public async Task Listing_OrderedById()
	{
		var first = await Users.CreateAsync(new UserData { Name = "order_one" });
		var second =
			await Users.CreateAsync(new UserData { Name = "order_two" });
		var third = await Users.CreateAsync(new UserData { Name = "order_three" });

		var page = await Users.ListAsync(new UserQuery(), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(new[] { first.Id, second.Id, third.Id }, page.Items.Select(account => account.Id));
	}

	/// <summary>Deletes an account.</summary>
	private async Task DeleteAsync(UserRecord user)
	{
		user.Value.DeletedAt = DateTimeOffset.UtcNow;
		await Users.UpdateAsync(user);
	}

	/// <summary>Creates two accounts and deletes the second one.</summary>
	private async Task TwoAccountsOneDeletedAsync()
	{
		await Users.CreateAsync(new UserData { Name = "living_user" });
		var deleted =
			await Users.CreateAsync(new UserData { Name = "dead_user" });
		await DeleteAsync(deleted);
	}
}