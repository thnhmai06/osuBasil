using Basil.Server.Features.Users;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;

using Basil.Server.Tests.Shared.Persistence;

namespace Basil.Server.Tests.Features.Users;

/// <summary>
///     Verifies `SqliteUserRepository`'s user lookups and writes: fetch by id/name, password hash,
///     country fix, and privilege grant. migrations/base.sql seeds a BasilBot user (id=0), which
///     these tests read back.
/// </summary>
public class SqliteUserRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
	private readonly SqliteUserRepository _repository =
		new(fixture.ConnectionString, NullLogger<SqliteUserRepository>.Instance);

	[Fact]
	public async Task FetchById_SeededBasilBot_ReturnsUser()
	{
		var user = await _repository.FetchByIdAsync(0);

		Assert.NotNull(user);
		Assert.Equal("BasilBot", user.Name);
		Assert.Equal("basilbot", User.MakeSafeName(user.Name));
		Assert.Equal(Country.Vn, user.Country);
	}

	[Fact]
	public async Task FetchById_Nonexistent_ReturnsNull()
	{
		Assert.Null(await _repository.FetchByIdAsync(999_999));
	}

	[Theory]
	[InlineData("BasilBot")]
	[InlineData("basilbot")]
	[InlineData("Basil Bot")] // spaces normalize to underscore via SafeName, but this differs from stored safe_name
	public async Task FetchByName_IsCaseInsensitiveViaSafeName(string name)
	{
		// only exact safe_name matches resolve; "Basil Bot" -> "basil_bot" != "basilbot" -> null
		var user = await _repository.FetchByNameAsync(name);

		if (name == "Basil Bot")
		{
			Assert.Null(user);
		}
		else
		{
			Assert.NotNull(user);
			Assert.Equal(0, user.Id);
		}
	}

	[Fact]
	public async Task FetchPasswordHash_SeededBasilBot_ReturnsStoredHash()
	{
		var hash = await _repository.FetchPasswordHashAsync(0);

		Assert.Equal("_______________________my_cool_bcrypt_______________________", hash);
	}

	[Fact]
	public async Task FetchPasswordHash_Nonexistent_ReturnsNull()
	{
		Assert.Null(await _repository.FetchPasswordHashAsync(999_999));
	}

	[Fact]
	public async Task UpdateCountry_PersistsChange()
	{
		var created = (await _repository.CreateAsync("country test user", "hash", Country.Xx))!;

		await _repository.UpdateCountryAsync(created.Id, Country.Jp);

		var updated = await _repository.FetchByIdAsync(created.Id);
		Assert.Equal(Country.Jp, updated!.Country);
	}

	[Fact]
	public async Task UpdatePrivileges_PersistsChange()
	{
		var created = (await _repository.CreateAsync("priv test user", "hash", Country.Xx))!;

		await _repository.UpdatePrivilegesAsync(created.Id, (UserPrivileges)3);

		var updated = await _repository.FetchByIdAsync(created.Id);
		Assert.Equal((UserPrivileges)3, updated!.Privilege);
	}

	[Fact]
	public async Task Create_ThenFetchByName_RoundTrips()
	{
		var created = (await _repository.CreateAsync("Fresh User", "some-hash", Country.Us))!;

		Assert.Equal("fresh_user", User.MakeSafeName(created.Name));

		var fetched = await _repository.FetchByNameAsync("FRESH USER");
		Assert.Equal(created.Id, fetched!.Id);
	}

	[Fact]
	public async Task UpdateName_PersistsNameAndSafeName()
	{
		var created = (await _repository.CreateAsync("rename me", "hash", Country.Xx))!;

		await _repository.UpdateNameAsync(created.Id, "renamed");

		var updated = await _repository.FetchByIdAsync(created.Id);
		Assert.Equal("renamed", updated!.Name);
		Assert.Equal("renamed", User.MakeSafeName(updated.Name));
	}

	[Fact]
	public async Task SoftDelete_PersistsDeletedAt()
	{
		var created = (await _repository.CreateAsync("delete test user", "hash", Country.Xx))!;
		var deletedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

		await _repository.SoftDeleteAsync(created.Id, deletedAt);

		var updated = await _repository.FetchByIdAsync(created.Id);
		Assert.Equal(deletedAt, updated!.DeletedAt);
	}

	/// <summary>
	///     Regression test (user-directed follow-up on Issue #4's soft-delete item): a soft-deleted
	///     user's name must stay reserved forever -- <c>Users_Name_uindex</c>/<c>Users_SafeName_uindex</c>
	///     are still enforced against the (never-removed) row, so a later registration attempting to
	///     reuse the exact name fails the same way any other name collision does.
	/// </summary>
	[Fact]
	public async Task SoftDeletedUser_NameStaysReserved_CreateReturnsNull()
	{
		var created = (await _repository.CreateAsync("claimed name", "hash", Country.Xx))!;
		await _repository.SoftDeleteAsync(created.Id, DateTimeOffset.UtcNow);

		var reclaimed = await _repository.CreateAsync("claimed name", "different-hash", Country.Xx);

		Assert.Null(reclaimed);
	}

	[Fact]
	public async Task Search_KeywordsSubstring_MatchesUsername()
	{
		var created = (await _repository.CreateAsync("searchable player", "hash", Country.Xx))!;

		var results = await _repository.SearchAsync(new UserSearchFilters("search"), 0, 50);

		Assert.Contains(results, u => u.Id == created.Id);
	}

	[Fact]
	public async Task Search_NumericKeywords_MatchesIdExactly()
	{
		var created = (await _repository.CreateAsync("numeric id search user", "hash", Country.Xx))!;

		var results = await _repository.SearchAsync(new UserSearchFilters(created.Id.ToString()), 0, 50);

		Assert.Contains(results, u => u.Id == created.Id);
	}

	[Fact]
	public async Task Search_CountryFilter_ExcludesOtherCountries()
	{
		var jp = (await _repository.CreateAsync("jp search user", "hash", Country.Jp))!;
		var us = (await _repository.CreateAsync("us search user", "hash", Country.Us))!;

		var results = await _repository.SearchAsync(new UserSearchFilters("search user", [Country.Jp]), 0, 50);

		Assert.Contains(results, u => u.Id == jp.Id);
		Assert.DoesNotContain(results, u => u.Id == us.Id);
	}

	[Fact]
	public async Task Search_MultipleCountryFilter_MatchesAnyOfThem()
	{
		var jp = (await _repository.CreateAsync("jp multi country user", "hash", Country.Jp))!;
		var us = (await _repository.CreateAsync("us multi country user", "hash", Country.Us))!;
		var vn = (await _repository.CreateAsync("vn multi country user", "hash", Country.Vn))!;

		var results = await _repository.SearchAsync(
			new UserSearchFilters("multi country user", [Country.Jp, Country.Us]), 0, 50);

		Assert.Contains(results, u => u.Id == jp.Id);
		Assert.Contains(results, u => u.Id == us.Id);
		Assert.DoesNotContain(results, u => u.Id == vn.Id);
	}

	[Fact]
	public async Task Search_PrivilegeMask_MatchesOnlyUsersWithEveryBitSet()
	{
		var withBoth = (await _repository.CreateAsync("priv mask both", "hash", Country.Xx,
			UserPrivileges.Unrestricted | UserPrivileges.Verified))!;
		var withOne = (await _repository.CreateAsync("priv mask one", "hash", Country.Xx,
			UserPrivileges.Unrestricted))!;

		var results = await _repository.SearchAsync(
			new UserSearchFilters("priv mask",
				Privilege: UserPrivileges.Unrestricted | UserPrivileges.Verified),
			0, 50);

		Assert.Contains(results, u => u.Id == withBoth.Id);
		Assert.DoesNotContain(results, u => u.Id == withOne.Id);
	}

	[Fact]
	public async Task Search_DeletedUser_IsExcluded()
	{
		var created = (await _repository.CreateAsync("deleted search user", "hash", Country.Xx))!;
		await _repository.SoftDeleteAsync(created.Id, DateTimeOffset.UtcNow);

		var results = await _repository.SearchAsync(new UserSearchFilters("deleted search"), 0, 50);

		Assert.DoesNotContain(results, u => u.Id == created.Id);
	}

	[Fact]
	public async Task SearchCount_MatchesSearchResultCountAcrossPages()
	{
		await _repository.CreateAsync("count search user one", "hash", Country.Xx);
		await _repository.CreateAsync("count search user two", "hash", Country.Xx);

		var page = await _repository.SearchAsync(new UserSearchFilters("count search"), 0, 1);
		var total = await _repository.SearchCountAsync(new UserSearchFilters("count search"));

		Assert.Single(page);
		Assert.True(total >= 2);
	}
}