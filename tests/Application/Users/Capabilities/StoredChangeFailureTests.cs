using Basil.Application.Common;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Capabilities;

/// <summary>A change to an account that the store refuses reaches the caller and leaves the account as it was.</summary>
public sealed class StoredChangeFailureTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task SetPermissionsAsync_WhenTheStoreRefuses_ThrowsAndKeepsThePermissions()
	{
		using var app = new TestApplication();
		var staff = await app.LoginAsync(await app.CreateUserAsync("PermStaff", "password", UserPermissionsGroups.All));
		var target = await app.CreateUserAsync("PermRefused", "password");
		var before = target.Value.Permissions;
		app.UserUpdateFailure = new InvalidOperationException("refused");

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			app.Users.Of(target).ScopeTo(staff).Permissions.SetAsync(UserPermissions.None, Token));

		Assert.Equal(before, target.Value.Permissions);
	}

	[Fact]
	public async Task SetCountryAsync_WhenTheStoreRefuses_ThrowsAndKeepsTheCountry()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("CountryRefused", "password");
		var actor = await app.LoginAsync(user);
		var before = user.Value.Country;
		app.UserUpdateFailure = new InvalidOperationException("refused");

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			app.Users.Of(user).ScopeTo(actor).Profile.SetCountryAsync(Country.Vn, Token));

		Assert.Equal(before, user.Value.Country);
	}

	[Fact]
	public async Task DeleteAsync_WhenTheStoreRefuses_ThrowsAndLeavesTheAccountUndeletedSoItCanBeRetried()
	{
		using var app = new TestApplication();
		var staff = await app.LoginAsync(
			await app.CreateUserAsync("DeleteStaff", "password", UserPermissionsGroups.All));
		var target = await app.CreateUserAsync("DeleteRefused", "password");
		var scoped = app.Users.Of(target).ScopeTo(staff);
		app.UserUpdateFailure = new InvalidOperationException("refused");

		await Assert.ThrowsAsync<InvalidOperationException>(() => scoped.DeleteAsync(Token));
		Assert.Null(target.Value.DeletedAt);

		app.UserUpdateFailure = null;
		Assert.True((await scoped.DeleteAsync(Token)).IsOk);
		Assert.NotNull(target.Value.DeletedAt);
	}

	[Fact]
	public async Task ListAsync_OfAnotherAccountsSessions_WhenTheAuditCannotBeStored_ThrowsInsteadOfReading()
	{
		using var app = new TestApplication();
		var target = await app.CreateUserAsync("AuditTarget", "password");
		await app.LoginAsync(target);
		var staff = await app.LoginAsync(await app.CreateUserAsync("AuditStaff", "password",
			UserPermissionsGroups.All));
		app.ModerationLogFailure = new InvalidOperationException("refused");

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			app.Users.Of(target).ScopeTo(staff).Sessions.ListAsync(new PageRequest(0, 10), cancellationToken: Token));

		Assert.Empty(app.ModerationEntries);
	}
}