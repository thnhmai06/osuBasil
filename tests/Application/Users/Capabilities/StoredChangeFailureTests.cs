using Basil.Application.Common;
using Basil.Domain.Users;
using Basil.Testing;
using Basil.Application.Users.Objects;

namespace Basil.Application.Users.Tests.Capabilities;

/// <summary>A change to an account that the store refuses reaches the caller and leaves the account as it was.</summary>
public sealed class StoredChangeFailureTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task PrivilegeStoreFailure_ThrowsKeepsPrivileges()
	{
		using var app = new TestApplication();
		var staff = await app.LoginAsync(await app.CreateUserAsync("PermStaff", "password", UserPrivilegesExtensions.All));
		var target = await app.CreateUserAsync("PermRefused", "password");
		var before = target.Value.Privileges;
		app.UserUpdateFailure = new InvalidOperationException("refused");

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			app.Users.Of(target).ScopeTo(staff).Privileges.SetAsync(UserPrivileges.None));

		Assert.Equal(before, target.Value.Privileges);
	}

	[Fact]
	public async Task CountryStoreFailure_ThrowsKeepsCountry()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("CountryRefused", "password");
		var actor = await app.LoginAsync(user);
		var before = user.Value.Country;
		app.UserUpdateFailure = new InvalidOperationException("refused");

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			app.Users.Of(user).ScopeTo(actor).Profile.SetCountryAsync(Country.Vn));

		Assert.Equal(before, user.Value.Country);
	}

	[Fact]
	public async Task DeleteStoreFailure_ThrowsUndeleted()
	{
		using var app = new TestApplication();
		var staff = await app.LoginAsync(
			await app.CreateUserAsync("DeleteStaff", "password", UserPrivilegesExtensions.All));
		var target = await app.CreateUserAsync("DeleteRefused", "password");
		var scoped = app.Users.Of(target).ScopeTo(staff);
		app.UserUpdateFailure = new InvalidOperationException("refused");

		await Assert.ThrowsAsync<InvalidOperationException>(() => scoped.DeleteAsync());
		Assert.Null(target.Value.DeletedAt);

		app.UserUpdateFailure = null;
		Assert.True((await scoped.DeleteAsync()) == UserOutcome.Ok);
		Assert.NotNull(target.Value.DeletedAt);
	}

	[Fact]
	public async Task AuditStoreFailure_ThrowsBeforeRead()
	{
		using var app = new TestApplication();
		var target = await app.CreateUserAsync("AuditTarget", "password");
		await app.LoginAsync(target);
		var staff = await app.LoginAsync(await app.CreateUserAsync("AuditStaff", "password",
			UserPrivilegesExtensions.All));
		app.ModerationLogFailure = new InvalidOperationException("refused");

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			app.Users.Of(target).ScopeTo(staff).Sessions.ListAsync(new PageRequest(0, 10), ct: Token));

		Assert.Empty(app.ModerationEntries);
	}
}