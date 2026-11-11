using System.Net;
using System.Text;
using Basil.Application.Common.Settings;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Persistence;
using Basil.Domain.Authentication;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Registry;

public sealed class UserRegistryTests
{
	[Fact]
	public async Task AccountsAreLocked_ReturnsLocked()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings { Locks = Locks.Accounts });

		var failure = await app.Users.CheckRegistrationAsync(
			new RegisterAttempt("LockedUser", new Md5("0123456789abcdef0123456789abcdef")),
			TestContext.Current.CancellationToken);

		Assert.Equal(RegistrationFailure.Locked, failure);
	}

	[Fact]
	public async Task AccountManager_BypassesLock()
	{
		using var app = new TestApplication();
		var manager = await app.CreateUserAsync("LockManager", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Owner);
		await app.SetSettingsAsync(new ServerSettings { Locks = Locks.Accounts });
		var actor = await app.LoginAsync(manager);

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(
			new UserData { Name = "StaffMade" }, new Md5("0123456789abcdef0123456789abcdef"), null);

		Assert.True(result.IsOk);
		Assert.NotNull(user);
	}

	[Fact]
	public async Task DefaultSettings_GrantsPlayerSupporter()
	{
		using var app = new TestApplication();

		var (user, failure) = await app.Users.RegisterAsync(
			new RegisterAttempt("DefaultMask", new Md5("0123456789abcdef0123456789abcdef")),
			TestContext.Current.CancellationToken);

		Assert.Null(failure);
		Assert.Equal(UserPrivilegesExtensions.Default, user!.Value.Privileges);
	}

	[Fact]
	public async Task DefaultsChanged_GrantsExactMask()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings
		{
			Accounts = new ServerSettings.AccountSettings { DefaultPrivileges = UserPrivileges.Player }
		});

		var (user, failure) = await app.Users.RegisterAsync(
			new RegisterAttempt("ChangedMask", new Md5("0123456789abcdef0123456789abcdef")),
			TestContext.Current.CancellationToken);

		Assert.Null(failure);
		Assert.Equal(UserPrivileges.Player, user!.Value.Privileges);
	}

	[Fact]
	public async Task UnknownName_ReturnsUnknownUser()
	{
		using var app = new TestApplication();

		var result = await app.AttemptLoginAsync("MissingUser", "password");

		Assert.Equal(LoginFailure.UnknownUser, result.Failure);
	}

	[Fact]
	public async Task WrongPassword_ReturnsWrongPassword()
	{
		using var app = new TestApplication();
		await app.CreateUserAsync("PasswordUser", "right-password");

		var result = await app.AttemptLoginAsync("PasswordUser", "wrong-password");

		Assert.Equal(LoginFailure.WrongPassword, result.Failure);
	}

	[Fact]
	public async Task DeletedAccount_ReturnsAccountDeleted()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("DeletedUser", "password");
		user.Value.DeletedAt = app.Clock.GetUtcNow();

		var result = await app.AttemptLoginAsync("DeletedUser", "password");

		Assert.Equal(LoginFailure.AccountDeleted, result.Failure);
	}

	[Fact]
	public async Task CorrectCredentials_PublishesCameOnline()
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
	public async Task RemovedBotKind_OutOfRange()
	{
		using var app = new TestApplication();

		// 16 was the value of the removed bot kind; no connection kind carries it any more.
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => app.Users.LoginAsync(
			new LoginAttempt("unused", new Md5("0123456789abcdef0123456789abcdef")), (SessionType)16,
			IPAddress.Loopback, null, 0, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task IrcAccountPassword_ThrowsArgument()
	{
		using var app = new TestApplication();
		await app.CreateUserAsync("IrcByPassword", "password");

		await Assert.ThrowsAsync<ArgumentException>(() => app.Users.LoginAsync(
			new LoginAttempt("IrcByPassword", new Md5(Encoding.UTF8.GetBytes("password"))),
			SessionType.Irc, IPAddress.Loopback, null, 0, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task OwnerRefused_OpensNothingKeys()
	{
		using var app = new TestApplication();
		var opened = await app.LoginAsync(await app.CreateUserAsync("KeyClaimed", "password"), SessionType.Irc);

		var refused = await app.Users.LoginWithTokenAsync(SessionType.Irc, opened.Token, IPAddress.Loopback,
			owner => owner.Value.Name == "SomeoneElse", TestContext.Current.CancellationToken);
		var accepted = await app.Users.LoginWithTokenAsync(SessionType.Irc, opened.Token, IPAddress.Loopback,
			owner => owner.Value.Name == "KeyClaimed", TestContext.Current.CancellationToken);

		Assert.Equal(LoginFailure.InvalidToken, refused.Failure);
		Assert.Same(opened, accepted.Connection);
	}

	[Fact]
	public async Task StoredKeyRefused_NotOnline()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("KeyStored", "password");
		var api = await app.LoginAsync(user);
		var (_, key, _) = await app.Users.Of(user).ScopeTo(api).Sessions
			.OpenIrcAsync("device");

		var refused = await app.Users.LoginWithTokenAsync(SessionType.Irc, key!, IPAddress.Loopback, _ => false,
			TestContext.Current.CancellationToken);

		Assert.Equal(LoginFailure.InvalidToken, refused.Failure);
		Assert.Empty(app.Users.Of(user).Ircs);
	}

	[Fact]
	public async Task BasilBotName_ReturnsUnknownUser()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();

		var result = await app.AttemptLoginAsync("BasilBot", "basil-bot");

		Assert.Equal(LoginFailure.UnknownUser, result.Failure);
	}

	[Fact]
	public async Task SecondStableLogin_TooManySessions()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("AlreadyOnline", "password");
		await app.LoginAsync(user, SessionType.Stable);
		app.Clock.Advance(TimeSpan.FromSeconds(9));

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", SessionType.Stable);

		Assert.Equal(LoginFailure.TooManySessions, result.Failure);
	}

	[Fact]
	public async Task SecondWhileOpen_TooManyIdle()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("StillOpen", "password");
		var old = await app.LoginAsync(user, SessionType.Stable);
		app.Clock.Advance(TimeSpan.FromSeconds(120));

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", SessionType.Stable);

		Assert.Equal(LoginFailure.TooManySessions, result.Failure);
		Assert.True(old.IsOpen);
	}

	[Fact]
	public async Task TourneyNoObserve_NoTourneyPrivilege()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("NoTourney", "password", UserPrivilegesExtensions.Default);

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", SessionType.StableTourney);

		Assert.Equal(LoginFailure.NoTourneyPrivilege, result.Failure);
	}

	[Theory]
	[InlineData(SessionType.Stable)]
	[InlineData(SessionType.StableTourney)]
	public async Task OsuNoClient_ThrowsArgumentNull(SessionType type)
	{
		using var app = new TestApplication();

		await Assert.ThrowsAsync<ArgumentNullException>(() => app.Users.LoginAsync(
			new LoginAttempt("unused", new Md5("0123456789abcdef0123456789abcdef")), type,
			IPAddress.Loopback, null, 0, TestContext.Current.CancellationToken));
	}
}