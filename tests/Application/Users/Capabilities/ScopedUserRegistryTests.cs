using Basil.Application.Content.Persistence;
using Basil.Application.Users.Objects;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
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
			new UserData { Name = "NewAccount" }, Hash, null);

		Assert.Null(user);
		Assert.Equal(UserPrivileges.Owner, result.Missing);
	}

	[Fact]
	public async Task CreateAccountAsync_RequestedPermissionNotHeld_NamesTheMissingPermission()
	{
		using var app = new TestApplication();
		var privileges = UserPrivilegesExtensions.Default | UserPrivileges.Owner;
		var actorRecord = await app.CreateUserAsync("LimitedMod", "password", privileges);
		var actor = await app.LoginAsync(actorRecord);

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(
			new UserData { Name = "TooMuchPower" }, Hash, UserPrivileges.Developer);

		Assert.Null(user);
		Assert.Equal(UserPrivileges.Developer, result.Missing);
	}

	[Fact]
	public async Task CreateAccountAsync_AllowedPermissionsWithinActorsGrant_CreatesAccount()
	{
		using var app = new TestApplication();
		var actorRecord = await app.CreateUserAsync("AccountManager", "password", UserPrivilegesExtensions.All);
		var actor = await app.LoginAsync(actorRecord);
		var data = new UserData { Name = "CreatedAccount", Privileges = UserPrivileges.Moderator };

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(data, Hash, data.Privileges);

		Assert.True(result.IsOk);
		Assert.NotNull(user);
		Assert.Equal(UserPrivileges.Moderator, user.Value.Privileges);
	}

	[Fact]
	public async Task CreateAccountAsync_NoPermissionsAsked_GrantsTheConfiguredDefault()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("DefaultMgr", "password",
			UserPrivilegesExtensions.All));
		await app.SetSettingsAsync(new ServerSettings
		{
			Accounts = new ServerSettings.AccountSettings { DefaultPrivileges = UserPrivileges.Player }
		});

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(
			new UserData { Name = "ConfigDefault" }, Hash, null);

		Assert.True(result.IsOk);
		Assert.Equal(UserPrivileges.Player, user!.Value.Privileges);
	}

	[Fact]
	public async Task CreateAccountAsync_PermissionsAsked_WinOverTheConfiguredDefault()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("ExplicitMgr", "password",
			UserPrivilegesExtensions.All));
		await app.SetSettingsAsync(new ServerSettings
		{
			Accounts = new ServerSettings.AccountSettings { DefaultPrivileges = UserPrivileges.Player }
		});

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(
			new UserData { Name = "ExplicitMask" }, Hash, UserPrivileges.Moderator);

		Assert.True(result.IsOk);
		Assert.Equal(UserPrivileges.Moderator, user!.Value.Privileges);
	}

	[Fact]
	public async Task CreateAccountAsync_DuplicateName_ReturnsNameTaken()
	{
		using var app = new TestApplication();
		var actorRecord = await app.CreateUserAsync("DupManager", "password", UserPrivilegesExtensions.All);
		await app.CreateUserAsync("AlreadyCreated", "password");
		var actor = await app.LoginAsync(actorRecord);

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(
			new UserData { Name = "AlreadyCreated" }, Hash, null);

		Assert.Null(user);
		Assert.Equal(UserOutcome.NameTaken, result.Outcome);
	}
}