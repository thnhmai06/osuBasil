using Basil.Application.Runtime.Users;
using Basil.Testing;
using Basil.Domain.Common;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Tests.Users;

public sealed class ScopedUserRegistryTests
{
	[Fact]
	public async Task CreateAccountAsync_ActorLacksUserManage_NamesTheMissingPermission()
	{
		using var app = new TestApplication();
		var actor = await app.CreateUserAsync("NoAccountManage", "password");
		var by = await app.LoginAsync(actor);

		var (user, result) = await app.Users.ScopeTo(by).CreateAccountAsync(
			new UserData { Name = "NewAccount" }, Hash, TestContext.Current.CancellationToken);

		Assert.Null(user);
		Assert.Equal(Permissions.UserManage, result.Missing);
	}

	[Fact]
	public async Task CreateAccountAsync_RequestedPermissionNotHeld_NamesTheMissingPermission()
	{
		using var app = new TestApplication();
		var permissions = PermissionsGroups.Default | Permissions.UserManage;
		var actor = await app.CreateUserAsync("LimitedMod", "password", permissions);
		var by = await app.LoginAsync(actor);

		var (user, result) = await app.Users.ScopeTo(by).CreateAccountAsync(
			new UserData { Name = "TooMuchPower", Permissions = Permissions.ServerConfigure }, Hash,
			TestContext.Current.CancellationToken);

		Assert.Null(user);
		Assert.Equal(Permissions.ServerConfigure, result.Missing);
	}

	[Fact]
	public async Task CreateAccountAsync_AllowedPermissionsWithinActorsGrant_CreatesAccount()
	{
		using var app = new TestApplication();
		var actor = await app.CreateUserAsync("AccountManager", "password", PermissionsGroups.All);
		var by = await app.LoginAsync(actor);
		var data = new UserData { Name = "CreatedAccount", Permissions = Permissions.UserNote };

		var (user, result) = await app.Users.ScopeTo(by).CreateAccountAsync(data, Hash,
			TestContext.Current.CancellationToken);

		Assert.True(result.IsOk);
		Assert.NotNull(user);
		Assert.Equal(Permissions.UserNote, user.Value.Permissions);
	}

	[Fact]
	public async Task CreateAccountAsync_DuplicateName_ReturnsNameTaken()
	{
		using var app = new TestApplication();
		var actor = await app.CreateUserAsync("DupManager", "password", PermissionsGroups.All);
		await app.CreateUserAsync("AlreadyCreated", "password");
		var by = await app.LoginAsync(actor);

		var (user, result) = await app.Users.ScopeTo(by).CreateAccountAsync(
			new UserData { Name = "AlreadyCreated" }, Hash, TestContext.Current.CancellationToken);

		Assert.Null(user);
		Assert.Equal(UserOutcome.NameTaken, result.Outcome);
	}

	private static Md5 Hash => new("0123456789abcdef0123456789abcdef");
}