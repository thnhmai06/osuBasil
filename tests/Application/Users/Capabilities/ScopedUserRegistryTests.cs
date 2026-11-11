using Basil.Application.Shared.Settings;
using Basil.Application.Users.Objects;
using Basil.Domain.Shared;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Capabilities;

public sealed class ScopedUserRegistryTests
{
	private static Md5 Hash => new("0123456789abcdef0123456789abcdef");

	[Fact]
	public async Task CreateNoManage_Named()
	{
		using var app = new TestApplication();
		var actorRecord = await app.CreateUserAsync("NoAccountManage", "password");
		var actor = await app.LoginAsync(actorRecord);

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(
			new UserData { Name = "NewAccount" }, Hash, null);

		Assert.Null(user);
		Assert.Equal(UserOutcome.Denied, result);
	}

	[Fact]
	public async Task CreateNotHeld_Named()
	{
		using var app = new TestApplication();
		var privileges = UserPrivilegesExtensions.Default | UserPrivileges.Owner;
		var actorRecord = await app.CreateUserAsync("LimitedMod", "password", privileges);
		var actor = await app.LoginAsync(actorRecord);

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(
			new UserData { Name = "TooMuchPower" }, Hash, UserPrivileges.Developer);

		Assert.Null(user);
		Assert.Equal(UserOutcome.Denied, result);
	}

	[Fact]
	public async Task CreateWithinGrant_Created()
	{
		using var app = new TestApplication();
		var actorRecord = await app.CreateUserAsync("AccountManager", "password", UserPrivilegesExtensions.All);
		var actor = await app.LoginAsync(actorRecord);
		var data = new UserData { Name = "CreatedAccount", Privileges = UserPrivileges.Moderator };

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(data, Hash, data.Privileges);

		Assert.True(result == UserOutcome.Ok);
		Assert.NotNull(user);
		Assert.Equal(UserPrivileges.Moderator, user.Value.Privileges);
	}

	[Fact]
	public async Task CreateNoPrivileges_Default()
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

		Assert.True(result == UserOutcome.Ok);
		Assert.Equal(UserPrivileges.Player, user!.Value.Privileges);
	}

	[Fact]
	public async Task CreateAsked_OverDefault()
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

		Assert.True(result == UserOutcome.Ok);
		Assert.Equal(UserPrivileges.Moderator, user!.Value.Privileges);
	}

	[Fact]
	public async Task CreateDuplicate_NameTaken()
	{
		using var app = new TestApplication();
		var actorRecord = await app.CreateUserAsync("DupManager", "password", UserPrivilegesExtensions.All);
		await app.CreateUserAsync("AlreadyCreated", "password");
		var actor = await app.LoginAsync(actorRecord);

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(
			new UserData { Name = "AlreadyCreated" }, Hash, null);

		Assert.Null(user);
		Assert.Equal(UserOutcome.NameTaken, result);
	}
}