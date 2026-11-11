using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Basil.Infrastructure.Users.Handlers;
using Basil.Testing;

namespace Basil.Infrastructure.Users.Tests.Handlers;

/// <summary>The messages that create an account and change its name, country, password, privileges and existence.</summary>
public sealed class AccountHandlerTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task CreateWithPrivilege_Stored()
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
	public async Task CreateNoPrivilege_Denied()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));

		var (user, result) = await new CreateAccountHandler(app.Users).Handle(
			new CreateAccount(actor, new UserData { Name = "Newcomer" }, Hash("secret"), null), Token);

		Assert.Null(user);
		Assert.Equal(UserPrivileges.Owner, result.Missing & UserPrivileges.Owner);
	}

	[Fact]
	public async Task RenameOwn_NameChanged()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Before", "password"));

		var result = await new RenameUserHandler(app.Users).Handle(new RenameUser(actor, UserReference.ById(actor.Record.Id), "After"), Token);

		Assert.True(result.IsOk);
		Assert.Equal("After", actor.User.Record.Value.Name);
	}

	[Fact]
	public async Task RenameOther_Denied()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"));

		var result = await new RenameUserHandler(app.Users).Handle(new RenameUser(actor, UserReference.ById(other.Record.Id), "Taken"), Token);

		Assert.Equal(UserPrivileges.Owner, result.Missing);
		Assert.Equal("Other", other.User.Record.Value.Name);
	}

	[Fact]
	public async Task CountryOwn_Changed()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Traveller", "password"));

		var result = await new SetUserCountryHandler(app.Users)
			.Handle(new SetUserCountry(actor, UserReference.ById(actor.Record.Id), Country.Vn), Token);

		Assert.True(result.IsOk);
		Assert.Equal(Country.Vn, actor.User.Record.Value.Country);
	}

	[Fact]
	public async Task CountryOther_Denied()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"));

		var result = await new SetUserCountryHandler(app.Users)
			.Handle(new SetUserCountry(actor, UserReference.ById(other.Record.Id), Country.Vn), Token);

		Assert.Equal(UserPrivileges.Owner, result.Missing);
	}

	[Fact]
	public async Task AwayOwn_Stored()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Away", "password"));

		var done = await new SetUserAwayHandler(app.Users).Handle(new SetUserAway(actor, UserReference.ById(actor.Record.Id), "brb"), Token);

		Assert.True(done);
		Assert.Equal("brb", actor.User.Profile.AwayMessage);
	}

	[Fact]
	public async Task AwayOther_Refused()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"));

		var done = await new SetUserAwayHandler(app.Users).Handle(new SetUserAway(actor, UserReference.ById(other.Record.Id), "brb"), Token);

		Assert.False(done);
		Assert.Null(other.User.Profile.AwayMessage);
	}

	[Fact]
	public async Task PmPrivateOwn_Stored()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Private", "password"));

		var done = await new SetUserPmPrivateHandler(app.Users)
			.Handle(new SetUserPmPrivate(actor, UserReference.ById(actor.Record.Id), true), Token);

		Assert.True(done);
		Assert.True(actor.User.Profile.PmPrivate);
	}

	[Fact]
	public async Task PmPrivateOther_Refused()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"));

		var done = await new SetUserPmPrivateHandler(app.Users)
			.Handle(new SetUserPmPrivate(actor, UserReference.ById(other.Record.Id), true), Token);

		Assert.False(done);
		Assert.False(other.User.Profile.PmPrivate);
	}

	[Fact]
	public async Task PasswordOwnCurrent_Replaced()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Careful", "old"), password: "old");

		var result = await new ChangeUserPasswordHandler(app.Users).Handle(
			new ChangeUserPassword(actor, UserReference.ById(actor.Record.Id), Hash("new"), Hash("old"), true), Token);

		Assert.True(result.IsOk);
		Assert.True(await app.VerifiesPasswordAsync(actor.Record, "new"));
	}

	[Fact]
	public async Task PasswordOwnWrong_Refused()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Careless", "old"), password: "old");

		var result = await new ChangeUserPasswordHandler(app.Users).Handle(
			new ChangeUserPassword(actor, UserReference.ById(actor.Record.Id), Hash("new"), Hash("wrong"), true), Token);

		Assert.Equal(UserOutcome.WrongPassword, result.Outcome);
		Assert.True(await app.VerifiesPasswordAsync(actor.Record, "old"));
	}

	[Fact]
	public async Task PrivilegesWithPrivilege_Replaced()
	{
		using var app = new TestApplication();
		var admin = await AdminAsync(app);
		var target = await app.LoginAsync(await app.CreateUserAsync("Target", "password"));

		var result = await new SetUserPrivilegesHandler(app.Users)
			.Handle(new SetUserPrivileges(admin, UserReference.ById(target.Record.Id), UserPrivileges.Player), Token);

		Assert.True(result.IsOk);
		Assert.Equal(UserPrivileges.Player, target.User.Record.Value.Privileges);
	}

	[Fact]
	public async Task PrivilegesNoPrivilege_Denied()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var target = await app.LoginAsync(await app.CreateUserAsync("Target", "password"));
		var before = target.User.Record.Value.Privileges;

		var result = await new SetUserPrivilegesHandler(app.Users)
			.Handle(new SetUserPrivileges(actor, UserReference.ById(target.Record.Id), UserPrivileges.Player), Token);

		Assert.NotNull(result.Missing);
		Assert.Equal(before, target.User.Record.Value.Privileges);
	}

	[Fact]
	public async Task DeleteWithPrivilege_Deleted()
	{
		using var app = new TestApplication();
		var admin = await AdminAsync(app);
		var target = await app.CreateUserAsync("Doomed", "password");

		var result = await new DeleteUserHandler(app.Users).Handle(new DeleteUser(admin, UserReference.ById(target.Id)), Token);

		Assert.True(result.IsOk);
		Assert.NotNull(app.Users.Of(target).Record.Value.DeletedAt);
	}

	[Fact]
	public async Task DeleteNoPrivilege_Denied()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var target = await app.CreateUserAsync("Safe", "password");

		var result = await new DeleteUserHandler(app.Users).Handle(new DeleteUser(actor, UserReference.ById(target.Id)), Token);

		Assert.Equal(UserPrivileges.Owner, result.Missing);
	}

	[Fact]
	public async Task RemoveAvatarOther_Denied()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var other = await app.CreateUserAsync("Other", "password");

		var result = await new RemoveUserAvatarHandler(app.Users).Handle(new RemoveUserAvatar(actor, UserReference.ById(other.Id)), Token);

		Assert.Equal(UserPrivileges.Owner, result.Missing);
	}

	[Fact]
	public async Task SetAvatarOther_Denied()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var other = await app.CreateUserAsync("Other", "password");

		var result = await new SetUserAvatarHandler(app.Users)
			.Handle(new SetUserAvatar(actor, UserReference.ById(other.Id), new MemoryStream()), Token);

		Assert.Equal(UserPrivileges.Owner, result.Missing);
	}

	private static async Task<Connection> AdminAsync(TestApplication app)
	{
		return await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPrivilegesExtensions.All));
	}

	private static Md5 Hash(string password)
	{
		return new Md5(System.Text.Encoding.UTF8.GetBytes(password));
	}
}
