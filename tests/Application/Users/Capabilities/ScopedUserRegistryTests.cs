using Basil.Application.Users.Objects;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Capabilities;

public sealed class ScopedUserRegistryTests
{
	private static Md5 Hash => new("0123456789abcdef0123456789abcdef");

	[Fact]
	public async Task CreateAccountAsync_ActorLacksUserManage_NamesTheMissingPermission()
	{
		using var app = new TestApplication();
		var actorRecord = await app.CreateUserAsync("NoAccountManage", "password");
		var actor = await app.LoginAsync(actorRecord);

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(
			new UserData { Name = "NewAccount" }, Hash, TestContext.Current.CancellationToken);

		Assert.Null(user);
		Assert.Equal(UserPermissions.UserManage, result.Missing);
	}

	[Fact]
	public async Task CreateAccountAsync_RequestedPermissionNotHeld_NamesTheMissingPermission()
	{
		using var app = new TestApplication();
		var permissions = UserPermissionsGroups.Default | UserPermissions.UserManage;
		var actorRecord = await app.CreateUserAsync("LimitedMod", "password", permissions);
		var actor = await app.LoginAsync(actorRecord);

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(
			new UserData { Name = "TooMuchPower", Permissions = UserPermissions.ServerManageSettings }, Hash,
			TestContext.Current.CancellationToken);

		Assert.Null(user);
		Assert.Equal(UserPermissions.ServerManageSettings, result.Missing);
	}

	[Fact]
	public async Task CreateAccountAsync_AllowedPermissionsWithinActorsGrant_CreatesAccount()
	{
		using var app = new TestApplication();
		var actorRecord = await app.CreateUserAsync("AccountManager", "password", UserPermissionsGroups.All);
		var actor = await app.LoginAsync(actorRecord);
		var data = new UserData { Name = "CreatedAccount", Permissions = UserPermissions.UserManageNote };

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(data, Hash,
			TestContext.Current.CancellationToken);

		Assert.True(result.IsOk);
		Assert.NotNull(user);
		Assert.Equal(UserPermissions.UserManageNote, user.Value.Permissions);
	}

	[Fact]
	public async Task CreateAccountAsync_DuplicateName_ReturnsNameTaken()
	{
		using var app = new TestApplication();
		var actorRecord = await app.CreateUserAsync("DupManager", "password", UserPermissionsGroups.All);
		await app.CreateUserAsync("AlreadyCreated", "password");
		var actor = await app.LoginAsync(actorRecord);

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(
			new UserData { Name = "AlreadyCreated" }, Hash, TestContext.Current.CancellationToken);

		Assert.Null(user);
		Assert.Equal(UserOutcome.NameTaken, result.Outcome);
	}
}