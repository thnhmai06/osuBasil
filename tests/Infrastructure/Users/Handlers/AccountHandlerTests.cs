using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Basil.Infrastructure.Users.Handlers;
using Basil.Testing;

namespace Basil.Infrastructure.Users.Tests.Handlers;

/// <summary>The messages that create an account and change its name, country, password, permissions and existence.</summary>
public sealed class AccountHandlerTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task CreateAccount_WithTheRightToManageUsers_StoresTheAccount()
	{
		using var app = new TestApplication();
		var admin = await AdminAsync(app);

		var (user, result) = await new CreateAccountHandler(app.Users).Handle(
			new CreateAccount(admin, new UserData { Name = "Newcomer" }, Hash("secret"), null), Token);

		Assert.True(result.IsOk);
		Assert.Equal("Newcomer", user!.Value.Name);
		Assert.True(await app.VerifiesPasswordAsync(user, "secret"));
	}

	[Fact]
	public async Task CreateAccount_WithoutTheRightToManageUsers_IsDeniedAndStoresNothing()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));

		var (user, result) = await new CreateAccountHandler(app.Users).Handle(
			new CreateAccount(actor, new UserData { Name = "Newcomer" }, Hash("secret"), null), Token);

		Assert.Null(user);
		Assert.Equal(UserPermissions.UserManage, result.Missing & UserPermissions.UserManage);
	}

	[Fact]
	public async Task RenameUser_OfTheSendersOwnAccount_ChangesTheName()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Before", "password"));

		var result = await new RenameUserHandler(app.Users).Handle(new RenameUser(actor, actor.Record, "After"), Token);

		Assert.True(result.IsOk);
		Assert.Equal("After", actor.User.Record.Value.Name);
	}

	[Fact]
	public async Task RenameUser_OfAnotherAccountWithoutTheRight_IsDeniedAndKeepsTheName()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"));

		var result = await new RenameUserHandler(app.Users).Handle(new RenameUser(actor, other.Record, "Taken"), Token);

		Assert.Equal(UserPermissions.UserManage, result.Missing);
		Assert.Equal("Other", other.User.Record.Value.Name);
	}

	[Fact]
	public async Task SetUserCountry_OfTheSendersOwnAccount_ChangesTheCountry()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Traveller", "password"));

		var result = await new SetUserCountryHandler(app.Users)
			.Handle(new SetUserCountry(actor, actor.Record, Country.Vn), Token);

		Assert.True(result.IsOk);
		Assert.Equal(Country.Vn, actor.User.Record.Value.Country);
	}

	[Fact]
	public async Task SetUserCountry_OfAnotherAccountWithoutTheRight_IsDenied()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"));

		var result = await new SetUserCountryHandler(app.Users)
			.Handle(new SetUserCountry(actor, other.Record, Country.Vn), Token);

		Assert.Equal(UserPermissions.UserManage, result.Missing);
	}

	[Fact]
	public async Task SetUserAway_ForTheSendersOwnAccount_StoresTheMessage()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Away", "password"));

		var done = await new SetUserAwayHandler(app.Users).Handle(new SetUserAway(actor, actor.Record, "brb"), Token);

		Assert.True(done);
		Assert.Equal("brb", actor.User.Profile.AwayMessage);
	}

	[Fact]
	public async Task SetUserAway_ForAnotherAccount_IsRefusedAndStoresNothing()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"));

		var done = await new SetUserAwayHandler(app.Users).Handle(new SetUserAway(actor, other.Record, "brb"), Token);

		Assert.False(done);
		Assert.Null(other.User.Profile.AwayMessage);
	}

	[Fact]
	public async Task SetUserPmPrivate_ForTheSendersOwnAccount_StoresTheSetting()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Private", "password"));

		var done = await new SetUserPmPrivateHandler(app.Users)
			.Handle(new SetUserPmPrivate(actor, actor.Record, true), Token);

		Assert.True(done);
		Assert.True(actor.User.Profile.PmPrivate);
	}

	[Fact]
	public async Task SetUserPmPrivate_ForAnotherAccount_IsRefusedAndStoresNothing()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"));

		var done = await new SetUserPmPrivateHandler(app.Users)
			.Handle(new SetUserPmPrivate(actor, other.Record, true), Token);

		Assert.False(done);
		Assert.False(other.User.Profile.PmPrivate);
	}

	[Fact]
	public async Task ChangeUserPassword_OfTheSendersOwnAccountWithTheCurrentPassword_ReplacesThePassword()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Careful", "old"), password: "old");

		var result = await new ChangeUserPasswordHandler(app.Users).Handle(
			new ChangeUserPassword(actor, actor.Record, Hash("new"), Hash("old"), true), Token);

		Assert.True(result.IsOk);
		Assert.True(await app.VerifiesPasswordAsync(actor.Record, "new"));
	}

	[Fact]
	public async Task ChangeUserPassword_OfTheSendersOwnAccountWithAWrongCurrentPassword_IsRefused()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Careless", "old"), password: "old");

		var result = await new ChangeUserPasswordHandler(app.Users).Handle(
			new ChangeUserPassword(actor, actor.Record, Hash("new"), Hash("wrong"), true), Token);

		Assert.Equal(UserOutcome.WrongPassword, result.Outcome);
		Assert.True(await app.VerifiesPasswordAsync(actor.Record, "old"));
	}

	[Fact]
	public async Task SetUserPermissions_WithTheRightToManagePermissions_ReplacesThePermissions()
	{
		using var app = new TestApplication();
		var admin = await AdminAsync(app);
		var target = await app.LoginAsync(await app.CreateUserAsync("Target", "password"));

		var result = await new SetUserPermissionsHandler(app.Users)
			.Handle(new SetUserPermissions(admin, target.Record, UserPermissions.ChatPost), Token);

		Assert.True(result.IsOk);
		Assert.Equal(UserPermissions.ChatPost, target.User.Record.Value.Permissions);
	}

	[Fact]
	public async Task SetUserPermissions_WithoutTheRight_IsDeniedAndKeepsThePermissions()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var target = await app.LoginAsync(await app.CreateUserAsync("Target", "password"));
		var before = target.User.Record.Value.Permissions;

		var result = await new SetUserPermissionsHandler(app.Users)
			.Handle(new SetUserPermissions(actor, target.Record, UserPermissions.ChatPost), Token);

		Assert.NotNull(result.Missing);
		Assert.Equal(before, target.User.Record.Value.Permissions);
	}

	[Fact]
	public async Task DeleteUser_WithTheRightToManageUsers_DeletesTheAccount()
	{
		using var app = new TestApplication();
		var admin = await AdminAsync(app);
		var target = await app.CreateUserAsync("Doomed", "password");

		var result = await new DeleteUserHandler(app.Users).Handle(new DeleteUser(admin, target), Token);

		Assert.True(result.IsOk);
		Assert.NotNull(app.Users.Of(target).Record.Value.DeletedAt);
	}

	[Fact]
	public async Task DeleteUser_WithoutTheRight_IsDeniedAndKeepsTheAccount()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var target = await app.CreateUserAsync("Safe", "password");

		var result = await new DeleteUserHandler(app.Users).Handle(new DeleteUser(actor, target), Token);

		Assert.Equal(UserPermissions.UserManage, result.Missing);
	}

	[Fact]
	public async Task RemoveUserAvatar_OfAnotherAccountWithoutTheRight_IsDenied()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var other = await app.CreateUserAsync("Other", "password");

		var result = await new RemoveUserAvatarHandler(app.Users).Handle(new RemoveUserAvatar(actor, other), Token);

		Assert.Equal(UserPermissions.UserManage, result.Missing);
	}

	[Fact]
	public async Task SetUserAvatar_OfAnotherAccountWithoutTheRight_IsDenied()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var other = await app.CreateUserAsync("Other", "password");

		var result = await new SetUserAvatarHandler(app.Users)
			.Handle(new SetUserAvatar(actor, other, new MemoryStream()), Token);

		Assert.Equal(UserPermissions.UserManage, result.Missing);
	}

	private static async Task<Connection> AdminAsync(TestApplication app)
	{
		return await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
	}

	private static Md5 Hash(string password)
	{
		return new Md5(System.Text.Encoding.UTF8.GetBytes(password));
	}
}
