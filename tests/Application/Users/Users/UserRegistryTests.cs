using System.Net;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Users;
using Basil.Application.Content.Persistence;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Users;

public sealed class UserRegistryTests
{
	[Fact]
	public async Task CheckRegistrationAsync_AccountsAreLocked_ReturnsLocked()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings { Locks = Locks.Accounts });

		var failure = await app.Users.CheckRegistrationAsync(
			new RegisterAttempt("LockedUser", new Md5("0123456789abcdef0123456789abcdef")),
			TestContext.Current.CancellationToken);

		Assert.Equal(RegistrationFailure.Locked, failure);
	}

	[Fact]
	public async Task CreateAccountAsync_AccountManagerBypassesAccountLock()
	{
		using var app = new TestApplication();
		var manager = await app.CreateUserAsync("LockManager", "password",
			UserPermissionsGroups.Default | UserPermissions.UserManage);
		await app.SetSettingsAsync(new ServerSettings { Locks = Locks.Accounts });
		var by = await app.LoginAsync(manager);

		var (user, result) = await app.Users.ScopeTo(by).CreateAccountAsync(
			new UserData { Name = "StaffMade" }, new Md5("0123456789abcdef0123456789abcdef"),
			TestContext.Current.CancellationToken);

		Assert.True(result.IsOk);
		Assert.NotNull(user);
	}

	[Fact]
	public async Task LoginAsync_UnknownName_ReturnsUnknownUser()
	{
		using var app = new TestApplication();

		var result = await app.AttemptLoginAsync("MissingUser", "password");

		Assert.Equal(LoginFailure.UnknownUser, result.Failure);
	}

	[Fact]
	public async Task LoginAsync_WrongPassword_ReturnsWrongPassword()
	{
		using var app = new TestApplication();
		await app.CreateUserAsync("PasswordUser", "right-password");

		var result = await app.AttemptLoginAsync("PasswordUser", "wrong-password");

		Assert.Equal(LoginFailure.WrongPassword, result.Failure);
	}

	[Fact]
	public async Task LoginAsync_DeletedAccount_ReturnsAccountDeleted()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("DeletedUser", "password");
		user.Value.DeletedAt = app.Clock.GetUtcNow();

		var result = await app.AttemptLoginAsync("DeletedUser", "password");

		Assert.Equal(LoginFailure.AccountDeleted, result.Failure);
	}

	[Fact]
	public async Task LoginAsync_CorrectCredentials_PublishesCameOnline()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("OnlineUser", "password");

		var result = await app.AttemptLoginAsync("OnlineUser", "password");

		Assert.True(result.Succeeded);
		Assert.True(result.Connection!.IsOpen);
		Assert.Same(app.Users.Online[user], result.Connection.User);
		var opened = Assert.IsType<UserConnectionOpened>(app.UserEvents.Events[^1]);
		Assert.Same(result.Connection, opened.Connection);
		Assert.True(opened.CameOnline);
	}

	[Fact]
	public async Task LoginAsync_BotConnectionRequested_ThrowsArgumentException()
	{
		using var app = new TestApplication();

		await Assert.ThrowsAsync<ArgumentException>(() => app.Users.LoginAsync(
			new LoginAttempt("unused", new Md5("0123456789abcdef0123456789abcdef")), ConnectionType.Bot,
			IPAddress.Loopback, null, 0, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task LoginAsync_IrcWithTheAccountPassword_ThrowsArgumentException()
	{
		using var app = new TestApplication();
		await app.CreateUserAsync("IrcByPassword", "password");

		await Assert.ThrowsAsync<ArgumentException>(() => app.Users.LoginAsync(
			new LoginAttempt("IrcByPassword", new Md5(System.Text.Encoding.UTF8.GetBytes("password"))),
			ConnectionType.Irc, IPAddress.Loopback, null, 0, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task LoginWithTokenAsync_OwnerRefusedByTheCaller_OpensNothingForAnOpenOrAStoredKey()
	{
		using var app = new TestApplication();
		var opened = await app.LoginAsync(await app.CreateUserAsync("KeyClaimed", "password"), ConnectionType.Irc);

		var refused = await app.Users.LoginWithTokenAsync(ConnectionType.Irc, opened.Token, IPAddress.Loopback,
			owner => owner.Value.Name == "SomeoneElse", TestContext.Current.CancellationToken);
		var accepted = await app.Users.LoginWithTokenAsync(ConnectionType.Irc, opened.Token, IPAddress.Loopback,
			owner => owner.Value.Name == "KeyClaimed", TestContext.Current.CancellationToken);

		Assert.Equal(LoginFailure.InvalidToken, refused.Failure);
		Assert.Same(opened, accepted.Connection);
	}

	[Fact]
	public async Task LoginWithTokenAsync_StoredKeyWhoseOwnerIsRefused_IsNotBroughtOnline()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("KeyStored", "password");
		var api = await app.LoginAsync(user);
		var (_, key, _) = await app.Users.Of(user).ScopeTo(api).Sessions.OpenIrcAsync("device", TestContext.Current.CancellationToken);

		var refused = await app.Users.LoginWithTokenAsync(ConnectionType.Irc, key!, IPAddress.Loopback, _ => false,
			TestContext.Current.CancellationToken);

		Assert.Equal(LoginFailure.InvalidToken, refused.Failure);
		Assert.Empty(app.Users.Of(user).Ircs);
	}

	[Fact]
	public async Task LoginAsync_BasilBotName_ReturnsUnknownUser()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();

		var result = await app.AttemptLoginAsync("BasilBot", "basil-bot");

		Assert.Equal(LoginFailure.UnknownUser, result.Failure);
	}

	[Fact]
	public async Task LoginAsync_SecondBanchoLoginWithinIdleWindow_ReturnsAlreadyOnline()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("AlreadyOnline", "password");
		await app.LoginAsync(user, ConnectionType.Osu);
		app.Clock.Advance(TimeSpan.FromSeconds(9));

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", ConnectionType.Osu);

		Assert.Equal(LoginFailure.AlreadyOnline, result.Failure);
	}

	[Fact]
	public async Task LoginAsync_SecondBanchoLoginAfterIdleWindow_ReplacesConnection()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("ReplaceIdle", "password");
		var old = await app.LoginAsync(user, ConnectionType.Osu);
		app.Clock.Advance(TimeSpan.FromSeconds(10));

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", ConnectionType.Osu);

		Assert.True(result.Succeeded);
		Assert.False(old.IsOpen);
		Assert.NotSame(old, result.Connection);
		var closed = Assert.IsType<UserConnectionClosed>(app.UserEvents.Events
			.Last(@event => @event is UserConnectionClosed));
		Assert.Equal(SessionEndReason.Replaced, closed.Reason);
	}

	[Fact]
	public async Task LoginAsync_TourneyWithoutRoomObserve_ReturnsNoTourneyPermission()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("NoTourney", "password", UserPermissionsGroups.Default);

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", ConnectionType.Tourney);

		Assert.Equal(LoginFailure.NoTourneyPermission, result.Failure);
	}

	[Theory]
	[InlineData(ConnectionType.Osu)]
	[InlineData(ConnectionType.Tourney)]
	public async Task LoginAsync_OsuKindWithoutClient_ThrowsArgumentNullException(ConnectionType type)
	{
		using var app = new TestApplication();

		await Assert.ThrowsAsync<ArgumentNullException>(() => app.Users.LoginAsync(
			new LoginAttempt("unused", new Md5("0123456789abcdef0123456789abcdef")), type,
			IPAddress.Loopback, null, 0, TestContext.Current.CancellationToken));
	}
}
