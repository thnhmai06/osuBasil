using System.Net;
using Basil.Application.Auth.Persistence;
using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Auth;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Capabilities;

/// <summary>
///     Closing sessions remotely, from the table of the logout rules: a closed session cannot sign in again with what it
///     stored, online or offline; an osu! device is asked once for its password; an IRC key closes alone; staff close
///     everything of an account they outrank and leave a trace.
/// </summary>
public sealed class ScopedUserSessionsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static Task<LoginResult> OpenWithTokenAsync(TestApplication app, SessionType type, string token)
	{
		return app.Users.LoginWithTokenAsync(type, token, IPAddress.Loopback, cancellationToken: Token);
	}

	/// <summary>Opens an IRC key for a user acting on their own account.</summary>
	private static async Task<(string Key, SessionEntry Session)> OpenIrcAsync(TestApplication app,
		Connection by, string label)
	{
		var (result, key, session) = await app.Users.Of(by.Record).ScopeTo(by).Sessions.OpenIrcAsync(label, Token);
		Assert.True(result.IsOk);
		return (key!, session!);
	}

	[Fact]
	public async Task CloseAsync_OfAnOnlineApiSession_ClosesItAndTheTokenOpensNothingAgain()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Closer", "password");
		var current = await app.LoginAsync(user);
		var other = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(current).Sessions.CloseAsync(other.Session.Id, Token);

		Assert.True(result.IsOk);
		Assert.False(other.IsOpen);
		Assert.True(current.IsOpen);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, SessionType.Api, other.Token)).Failure);
	}

	[Fact]
	public async Task CloseAsync_OfAnOfflineApiSession_RevokesItsToken()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("OfflineCloser", "password");
		var current = await app.LoginAsync(user);
		var offline = await app.LoginAsync(user);
		app.Clock.Advance(TimeSpan.FromSeconds(301));
		app.Users.MarkActive(current);
		app.Users.CloseIdle();
		Assert.False(offline.IsOpen);

		var result = await app.Users.Of(user).ScopeTo(current).Sessions.CloseAsync(offline.Session.Id, Token);

		Assert.True(result.IsOk);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, SessionType.Api, offline.Token)).Failure);
	}

	[Fact]
	public async Task CloseAsync_OfAnOnlineOsuSession_ClosesItAndTheDeviceIsRefusedOnceAtItsNextLogin()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("DeviceUser", "password");
		var api = await app.LoginAsync(user);
		var osu = await app.LoginAsync(user, SessionType.Osu);

		var result = await app.Users.Of(user).ScopeTo(api).Sessions.CloseAsync(osu.Session.Id, Token);

		Assert.True(result.IsOk);
		Assert.False(osu.IsOpen);
		Assert.Equal(LoginFailure.PasswordResetRequired,
			(await app.AttemptLoginAsync("DeviceUser", "password", SessionType.Osu)).Failure);
		Assert.True((await app.AttemptLoginAsync("DeviceUser", "password", SessionType.Osu)).Succeeded);
	}

	[Fact]
	public async Task CloseAsync_OfAnOnlineSessionNotYetStored_StillClosesIt()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("UnstoredUser", "password");
		var api = await app.LoginAsync(user);
		var osu = await app.LoginAsync(user, SessionType.Osu);
		app.ForgetStoredSession(osu.Session.Id);

		var result = await app.Users.Of(user).ScopeTo(api).Sessions.CloseAsync(osu.Session.Id, Token);

		Assert.True(result.IsOk);
		Assert.False(osu.IsOpen);
	}

	[Fact]
	public async Task CloseAsync_OfAnOfflineOsuSession_StillMarksTheDevice()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("OfflineDevice", "password");
		var api = await app.LoginAsync(user);
		var osu = await app.LoginAsync(user, SessionType.Osu);
		app.Users.Close(osu, SessionEndReason.ConnectionLost);

		Assert.True((await app.Users.Of(user).ScopeTo(api).Sessions.CloseAsync(osu.Session.Id, Token)).IsOk);

		Assert.Equal(LoginFailure.PasswordResetRequired,
			(await app.AttemptLoginAsync("OfflineDevice", "password", SessionType.Osu)).Failure);
	}

	[Fact]
	public async Task CloseAsync_OfAnIrcKey_StopsThatKeyAndLeavesTheOthersWorking()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("IrcUser", "password");
		var api = await app.LoginAsync(user);
		var (firstKey, first) = await OpenIrcAsync(app, api, "laptop");
		var (secondKey, _) = await OpenIrcAsync(app, api, "vps");

		Assert.True((await app.Users.Of(user).ScopeTo(api).Sessions.CloseAsync(first.Id, Token)).IsOk);

		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, SessionType.Irc, firstKey)).Failure);
		Assert.True((await OpenWithTokenAsync(app, SessionType.Irc, secondKey)).Succeeded);
	}

	[Fact]
	public async Task CloseAsync_OfAnUnknownSession_IsNotFound()
	{
		using var app = new TestApplication();
		var api = await app.LoginAsync(await app.CreateUserAsync("Lost", "password"));

		var result = await app.Users.Of(api.Record).ScopeTo(api).Sessions.CloseAsync(Guid.NewGuid(), Token);

		Assert.Equal(UserOutcome.NotFound, result.Outcome);
	}

	[Fact]
	public async Task CloseAsync_OfAnotherUsersSession_IsNotFoundAndLeavesItOpen()
	{
		using var app = new TestApplication();
		var mine = await app.LoginAsync(await app.CreateUserAsync("Mine", "password"));
		var theirs = await app.LoginAsync(await app.CreateUserAsync("Theirs", "password"));

		var result = await app.Users.Of(mine.Record).ScopeTo(mine).Sessions.CloseAsync(theirs.Session.Id, Token);

		Assert.Equal(UserOutcome.NotFound, result.Outcome);
		Assert.True(theirs.IsOpen);
	}

	[Fact]
	public async Task CloseAsync_OfAnotherAccountsSession_NeedsUserManageAndHierarchy()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Target", "password");
		var session = await app.LoginAsync(user);
		var weak = await app.LoginAsync(await app.CreateUserAsync("Weak", "password"));
		var peer = await app.LoginAsync(await app.CreateUserAsync("Peer", "password", UserPermissionsGroups.All));
		var peerTarget = await app.CreateUserAsync("PeerTarget", "password", UserPermissionsGroups.All);
		var peerSession = await app.LoginAsync(peerTarget);

		var withoutPermission = await app.Users.Of(user).ScopeTo(weak).Sessions.CloseAsync(session.Session.Id, Token);
		var equalRank = await app.Users.Of(peerTarget).ScopeTo(peer).Sessions.CloseAsync(peerSession.Session.Id, Token);

		Assert.Equal(UserPermissions.UserManage, withoutPermission.Missing);
		Assert.Equal(UserOutcome.NotOutranking, equalRank.Outcome);
		Assert.True(session.IsOpen);
		Assert.True(peerSession.IsOpen);
	}

	[Fact]
	public async Task AnUserCanBeConnectedFromSeveralIrcClientsAtOnce_EachWithItsOwnKey()
	{
		using var app = new TestApplication();
		var api = await app.LoginAsync(await app.CreateUserAsync("Multi", "password"));
		var (laptopKey, laptop) = await OpenIrcAsync(app, api, "laptop");
		var (vpsKey, _) = await OpenIrcAsync(app, api, "vps");

		var first = (await OpenWithTokenAsync(app, SessionType.Irc, laptopKey)).Connection!;
		var second = (await OpenWithTokenAsync(app, SessionType.Irc, vpsKey)).Connection!;
		await app.Users.Of(api.Record).ScopeTo(api).Sessions.CloseAsync(laptop.Id, Token);

		Assert.NotSame(first, second);
		Assert.False(first.IsOpen);
		Assert.True(second.IsOpen);
		Assert.Same(second, Assert.Single(app.Users.Online[api.Record].Ircs));
	}

	[Fact]
	public async Task CloseAllAsync_KeepingTheCurrent_ClosesEveryOtherSessionOfEveryKind()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("AllOut", "password");
		var current = await app.LoginAsync(user);
		var otherApi = await app.LoginAsync(user);
		var osu = await app.LoginAsync(user, SessionType.Osu);
		var (ircKey, _) = await OpenIrcAsync(app, current, "bot");

		var result = await app.Users.Of(user).ScopeTo(current).Sessions.CloseAllAsync(true, Token);

		Assert.True(result.IsOk);
		Assert.True(current.IsOpen);
		Assert.False(otherApi.IsOpen);
		Assert.False(osu.IsOpen);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, SessionType.Api, otherApi.Token)).Failure);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, SessionType.Irc, ircKey)).Failure);
		Assert.True((await OpenWithTokenAsync(app, SessionType.Api, current.Token)).Succeeded);
	}

	[Fact]
	public async Task CloseAllAsync_NotKeepingTheCurrent_ClosesItToo()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("NoKeep", "password");
		var current = await app.LoginAsync(user);

		Assert.True((await app.Users.Of(user).ScopeTo(current).Sessions.CloseAllAsync(false, Token)).IsOk);

		Assert.False(current.IsOpen);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, SessionType.Api, current.Token)).Failure);
	}

	[Fact]
	public async Task CloseAllAsync_ByStaff_ClosesEverythingAndRecordsIt()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPermissionsGroups.All));
		var user = await app.CreateUserAsync("Target", "password");
		var api = await app.LoginAsync(user);
		var osu = await app.LoginAsync(user, SessionType.Osu);

		var result = await app.Users.Of(user).ScopeTo(admin).Sessions.CloseAllAsync(true, Token);

		Assert.True(result.IsOk);
		Assert.False(api.IsOpen);
		Assert.False(osu.IsOpen);
		var entry = Assert.Single(app.ModerationEntries, e => e.Action == ModerationAction.CloseSessions);
		Assert.Equal(admin.Record, entry.By);
		Assert.Equal(user, entry.Target);
		Assert.Equal(LoginFailure.PasswordResetRequired,
			(await app.AttemptLoginAsync("Target", "password", SessionType.Osu)).Failure);
	}

	[Fact]
	public async Task CloseAllAsync_ByAUserWithoutUserManage_NamesTheMissingPermission()
	{
		using var app = new TestApplication();
		var other = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var user = await app.CreateUserAsync("Victim", "password");
		var api = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(other).Sessions.CloseAllAsync(false, Token);

		Assert.Equal(UserPermissions.UserManage, result.Missing);
		Assert.True(api.IsOpen);
	}

	[Fact]
	public async Task CloseAllAsync_ByStaffOfEqualRank_IsRefused()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		var peer = await app.CreateUserAsync("Peer", "password", UserPermissionsGroups.All);
		var api = await app.LoginAsync(peer);

		var result = await app.Users.Of(peer).ScopeTo(admin).Sessions.CloseAllAsync(false, Token);

		Assert.Equal(UserOutcome.NotOutranking, result.Outcome);
		Assert.True(api.IsOpen);
	}

	[Fact]
	public async Task ListAsync_OfOwnSessions_NeedsNoPermission_AndFiltersByStateAndKind()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Lister", "password", UserPermissions.None);
		var api = await app.LoginAsync(user);
		var osu = await app.LoginAsync(user, SessionType.Osu);
		app.Users.Close(osu, SessionEndReason.ConnectionLost);
		var scoped = app.Users.Of(user).ScopeTo(api).Sessions;

		var (_, active) = await scoped.ListAsync(new PageRequest(0, 10), SessionState.Active, null, Token);
		var (_, ended) = await scoped.ListAsync(new PageRequest(0, 10), SessionState.Ended, null, Token);
		var (_, osuOnly) = await scoped.ListAsync(new PageRequest(0, 10), SessionState.Any, [SessionType.Osu], Token);

		Assert.Equal(api.Session.Id, Assert.Single(active!.Items).Id);
		Assert.Equal(osu.Session.Id, Assert.Single(ended!.Items).Id);
		Assert.Equal(osu.Session.Id, Assert.Single(osuOnly!.Items).Id);
	}

	[Fact]
	public async Task ListAsync_OfAnotherUsersSessions_NeedsTheViewPermissionAndIsRecorded()
	{
		using var app = new TestApplication();
		var target = await app.CreateUserAsync("Viewed", "password");
		await app.LoginAsync(target);
		var nosy = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var staff = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPermissionsGroups.All));

		var (denied, none) = await app.Users.Of(target).ScopeTo(nosy).Sessions.ListAsync(new PageRequest(0, 10),
			cancellationToken: Token);
		var (allowed, page) = await app.Users.Of(target).ScopeTo(staff).Sessions.ListAsync(new PageRequest(0, 10),
			cancellationToken: Token);

		Assert.Equal(UserPermissions.UserManageViewSessions, denied.Missing);
		Assert.Null(none);
		Assert.True(allowed.IsOk);
		Assert.Single(page!.Items);
		Assert.Single(app.ModerationEntries, e => e.Action == ModerationAction.ViewLogins);
	}

	[Fact]
	public async Task OpenIrcAsync_ReturnsAKeyThatOpensAnIrcConnectionOfThatSession()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("KeyOwner", "password");
		var api = await app.LoginAsync(user);

		var (key, session) = await OpenIrcAsync(app, api, "osu-ahr VPS");
		var result = await OpenWithTokenAsync(app, SessionType.Irc, key);

		Assert.Equal("osu-ahr VPS", session.ClientInfo);
		Assert.Equal(SessionType.Irc, session.Type);
		Assert.True(result.Succeeded);
		Assert.Equal(session.Id, result.Connection!.Session.Id);
		Assert.IsType<IrcConnection>(result.Connection);
	}

	[Fact]
	public async Task IrcKey_SurvivesTheConnectionClosing_AndWrongKeysOpenNothing()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Reconnect", "password");
		var api = await app.LoginAsync(user);
		var (key, _) = await OpenIrcAsync(app, api, "laptop");
		var first = (await OpenWithTokenAsync(app, SessionType.Irc, key)).Connection!;

		app.Users.Close(first, SessionEndReason.LoggedOut);

		Assert.True((await OpenWithTokenAsync(app, SessionType.Irc, key)).Succeeded);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, SessionType.Irc, key + "x")).Failure);
	}

	[Fact]
	public async Task OpenIrcAsync_ForAnotherUser_IsRefused()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPermissionsGroups.All));
		var user = await app.CreateUserAsync("Other", "password");

		var (result, key, _) = await app.Users.Of(user).ScopeTo(admin).Sessions.OpenIrcAsync("sneaky", Token);

		Assert.Equal(UserOutcome.NotSelf, result.Outcome);
		Assert.Null(key);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task OpenIrcAsync_WithoutALabel_IsRefused(string label)
	{
		using var app = new TestApplication();
		var api = await app.LoginAsync(await app.CreateUserAsync("Unlabeled", "password"));

		var (result, _, _) = await app.Users.Of(api.Record).ScopeTo(api).Sessions.OpenIrcAsync(label, Token);

		Assert.Equal(UserOutcome.InvalidLabel, result.Outcome);
	}

	[Fact]
	public async Task OpenIrcAsync_WithATooLongLabel_IsRefused()
	{
		using var app = new TestApplication();
		var api = await app.LoginAsync(await app.CreateUserAsync("LongLabel", "password"));

		var (result, _, _) = await app.Users.Of(api.Record).ScopeTo(api).Sessions
			.OpenIrcAsync(new string('x', 65), Token);

		Assert.Equal(UserOutcome.InvalidLabel, result.Outcome);
	}

	[Fact]
	public async Task BasilBot_SessionsCannotBeClosedOrOpened()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPermissionsGroups.All));
		var bot = await app.SeedBotAsync();
		await app.Users.ConnectBotAsync(Token);
		var scoped = app.Users.Of(bot).ScopeTo(admin).Sessions;

		Assert.Equal(UserOutcome.SystemAccount, (await scoped.CloseAsync(Guid.NewGuid(), Token)).Outcome);
		Assert.Equal(UserOutcome.SystemAccount, (await scoped.CloseAllAsync(false, Token)).Outcome);
		Assert.Equal(UserOutcome.SystemAccount, (await scoped.OpenIrcAsync("x", Token)).Result.Outcome);
		Assert.True(app.Users.Online.GetValueOrDefault(bot)?.Bot?.IsOpen);
	}
}
