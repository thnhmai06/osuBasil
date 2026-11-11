using System.Net;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Domain.Authentication;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Capabilities;

/// <summary>
///     Closing sessions remotely, from the table of the logout rules: a closed session cannot sign in again with what it
///     stored, online or offline; an osu! client is disconnected but signs in again with its password; an IRC key closes
///     alone; staff close everything of an account they outrank and leave a trace.
/// </summary>
public sealed class ScopedUserSessionsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static Task<LoginResult> OpenWithTokenAsync(TestApplication app, SessionType type, string token)
	{
		return app.Users.LoginWithTokenAsync(type, token, IPAddress.Loopback, ct: Token);
	}

	/// <summary>Opens an IRC key for a user acting on their own account.</summary>
	private static async Task<(string Key, SessionRecord Session)> OpenIrcAsync(TestApplication app,
		Connection actor, string label)
	{
		var (result, key, session) =
			await app.Users.Of(actor.Record).ScopeTo(actor).Sessions.OpenIrcAsync(label);
		Assert.True(result == UserOutcome.Ok);
		return (key!, session!);
	}

	[Fact]
	public async Task OnlineApi_TokenOpensNothing()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Closer", "password");
		var current = await app.LoginAsync(user);
		var other = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(current).Sessions.CloseAsync(other.Session.Id);

		Assert.True(result == UserOutcome.Ok);
		Assert.False(other.IsOpen);
		Assert.True(current.IsOpen);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, SessionType.Api, other.Token)).Failure);
	}

	[Fact]
	public async Task OfflineApi_RevokesToken()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("OfflineCloser", "password");
		var current = await app.LoginAsync(user);
		var offline = await app.LoginAsync(user);
		app.Clock.Advance(TimeSpan.FromSeconds(301));
		app.Users.MarkActive(current);
		app.Users.CloseIdle();
		Assert.False(offline.IsOpen);

		var result = await app.Users.Of(user).ScopeTo(current).Sessions.CloseAsync(offline.Session.Id);

		Assert.True(result == UserOutcome.Ok);
		Assert.Equal(LoginFailure.InvalidToken,
			(await OpenWithTokenAsync(app, SessionType.Api, offline.Token)).Failure);
	}

	[Fact]
	public async Task OnlineOsu_SignsInAgain()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("DeviceUser", "password");
		var api = await app.LoginAsync(user);
		var osu = await app.LoginAsync(user, SessionType.Stable);

		var result = await app.Users.Of(user).ScopeTo(api).Sessions.CloseAsync(osu.Session.Id);

		Assert.True(result == UserOutcome.Ok);
		Assert.False(osu.IsOpen);
		Assert.True((await app.AttemptLoginAsync("DeviceUser", "password", SessionType.Stable)).Succeeded);
	}

	[Fact]
	public async Task OnlineNotStored_StillCloses()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("UnstoredUser", "password");
		var api = await app.LoginAsync(user);
		var osu = await app.LoginAsync(user, SessionType.Stable);
		app.ForgetStoredSession(osu.Session.Id);

		var result = await app.Users.Of(user).ScopeTo(api).Sessions.CloseAsync(osu.Session.Id);

		Assert.True(result == UserOutcome.Ok);
		Assert.False(osu.IsOpen);
	}

	[Fact]
	public async Task IrcKey_OthersWork()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("IrcUser", "password");
		var api = await app.LoginAsync(user);
		var (firstKey, first) = await OpenIrcAsync(app, api, "laptop");
		var (secondKey, _) = await OpenIrcAsync(app, api, "vps");

		Assert.True((await app.Users.Of(user).ScopeTo(api).Sessions.CloseAsync(first.Id)) == UserOutcome.Ok);

		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, SessionType.Irc, firstKey)).Failure);
		Assert.True((await OpenWithTokenAsync(app, SessionType.Irc, secondKey)).Succeeded);
	}

	[Fact]
	public async Task UnknownSession_NotFound()
	{
		using var app = new TestApplication();
		var api = await app.LoginAsync(await app.CreateUserAsync("Lost", "password"));

		var result = await app.Users.Of(api.Record).ScopeTo(api).Sessions.CloseAsync(long.MaxValue);

		Assert.Equal(UserOutcome.NotFound, result);
	}

	[Fact]
	public async Task OthersSession_NotFoundOpen()
	{
		using var app = new TestApplication();
		var mine = await app.LoginAsync(await app.CreateUserAsync("Mine", "password"));
		var theirs = await app.LoginAsync(await app.CreateUserAsync("Theirs", "password"));

		var result = await app.Users.Of(mine.Record).ScopeTo(mine).Sessions.CloseAsync(theirs.Session.Id);

		Assert.Equal(UserOutcome.NotFound, result);
		Assert.True(theirs.IsOpen);
	}

	[Fact]
	public async Task OthersAccount_NeedsManageHierarchy()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Target", "password");
		var session = await app.LoginAsync(user);
		var weak = await app.LoginAsync(await app.CreateUserAsync("Weak", "password"));
		var peer = await app.LoginAsync(await app.CreateUserAsync("Peer", "password", UserPrivilegesExtensions.All));
		var peerTarget = await app.CreateUserAsync("PeerTarget", "password", UserPrivilegesExtensions.All);
		var peerSession = await app.LoginAsync(peerTarget);

		var withoutPermission = await app.Users.Of(user).ScopeTo(weak).Sessions.CloseAsync(session.Session.Id);
		var equalRank = await app.Users.Of(peerTarget).ScopeTo(peer).Sessions.CloseAsync(peerSession.Session.Id);

		Assert.Equal(UserOutcome.Denied, withoutPermission);
		Assert.Equal(UserOutcome.NotOutranking, equalRank);
		Assert.True(session.IsOpen);
		Assert.True(peerSession.IsOpen);
	}

	[Fact]
	public async Task SeveralIrcClients_EachOwnKey()
	{
		using var app = new TestApplication();
		var api = await app.LoginAsync(await app.CreateUserAsync("Multi", "password"));
		var (laptopKey, laptop) = await OpenIrcAsync(app, api, "laptop");
		var (vpsKey, _) = await OpenIrcAsync(app, api, "vps");

		var first = (await OpenWithTokenAsync(app, SessionType.Irc, laptopKey)).Connection!;
		var second = (await OpenWithTokenAsync(app, SessionType.Irc, vpsKey)).Connection!;
		await app.Users.Of(api.Record).ScopeTo(api).Sessions.CloseAsync(laptop.Id);

		Assert.NotSame(first, second);
		Assert.False(first.IsOpen);
		Assert.True(second.IsOpen);
		Assert.Same(second, Assert.Single(app.Users.Online[api.Record].Ircs));
	}

	[Fact]
	public async Task KeepCurrent_OthersAllKinds()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("AllOut", "password");
		var current = await app.LoginAsync(user);
		var otherApi = await app.LoginAsync(user);
		var osu = await app.LoginAsync(user, SessionType.Stable);
		var (ircKey, _) = await OpenIrcAsync(app, current, "bot");

		var result = await app.Users.Of(user).ScopeTo(current).Sessions.CloseAllAsync(true);

		Assert.True(result == UserOutcome.Ok);
		Assert.True(current.IsOpen);
		Assert.False(otherApi.IsOpen);
		Assert.False(osu.IsOpen);
		Assert.Equal(LoginFailure.InvalidToken,
			(await OpenWithTokenAsync(app, SessionType.Api, otherApi.Token)).Failure);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, SessionType.Irc, ircKey)).Failure);
		Assert.True((await OpenWithTokenAsync(app, SessionType.Api, current.Token)).Succeeded);
	}

	[Fact]
	public async Task NotKeepCurrent_ClosesToo()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("NoKeep", "password");
		var current = await app.LoginAsync(user);

		Assert.True((await app.Users.Of(user).ScopeTo(current).Sessions.CloseAllAsync(false)) == UserOutcome.Ok);

		Assert.False(current.IsOpen);
		Assert.Equal(LoginFailure.InvalidToken,
			(await OpenWithTokenAsync(app, SessionType.Api, current.Token)).Failure);
	}

	[Fact]
	public async Task ByStaff_ClosesAllRecorded()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPrivilegesExtensions.All));
		var user = await app.CreateUserAsync("Target", "password");
		var api = await app.LoginAsync(user);
		var osu = await app.LoginAsync(user, SessionType.Stable);

		var result = await app.Users.Of(user).ScopeTo(admin).Sessions.CloseAllAsync(true);

		Assert.True(result == UserOutcome.Ok);
		Assert.False(api.IsOpen);
		Assert.False(osu.IsOpen);
		var entry = Assert.Single(app.ModerationEntries, e => e.Action == ModerationAction.CloseSessions);
		Assert.Equal(admin.Record, entry.Actor);
		Assert.Equal(user, entry.Target);
	}

	[Fact]
	public async Task NoManage_NamesMissing()
	{
		using var app = new TestApplication();
		var other = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var user = await app.CreateUserAsync("Victim", "password");
		var api = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(other).Sessions.CloseAllAsync(false);

		Assert.Equal(UserOutcome.Denied, result);
		Assert.True(api.IsOpen);
	}

	[Fact]
	public async Task EqualRankStaff_Refused()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPrivilegesExtensions.All));
		var peer = await app.CreateUserAsync("Peer", "password", UserPrivilegesExtensions.All);
		var api = await app.LoginAsync(peer);

		var result = await app.Users.Of(peer).ScopeTo(admin).Sessions.CloseAllAsync(false);

		Assert.Equal(UserOutcome.NotOutranking, result);
		Assert.True(api.IsOpen);
	}

	[Fact]
	public async Task OwnSessions_NoPrivilegeFilters()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Lister", "password", UserPrivileges.None);
		var api = await app.LoginAsync(user);
		var osu = await app.LoginAsync(user, SessionType.Stable);
		app.Users.Close(osu, SessionEndReason.ConnectionLost);
		var scoped = app.Users.Of(user).ScopeTo(api).Sessions;

		var (_, active) = await scoped.ListAsync(new PageRequest(0, 10), SessionState.Active, null, Token);
		var (_, ended) = await scoped.ListAsync(new PageRequest(0, 10), SessionState.Ended, null, Token);
		var (_, osuOnly) =
			await scoped.ListAsync(new PageRequest(0, 10), SessionState.Any, [SessionType.Stable], Token);

		Assert.Equal(api.Session.Id, Assert.Single(active!.Items).Id);
		Assert.Equal(osu.Session.Id, Assert.Single(ended!.Items).Id);
		Assert.Equal(osu.Session.Id, Assert.Single(osuOnly!.Items).Id);
	}

	[Fact]
	public async Task OthersSessions_NeedsViewRecorded()
	{
		using var app = new TestApplication();
		var target = await app.CreateUserAsync("Viewed", "password");
		await app.LoginAsync(target);
		var nosy = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var staff = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPrivilegesExtensions.All));

		var (denied, none) = await app.Users.Of(target).ScopeTo(nosy).Sessions.ListAsync(new PageRequest(0, 10),
			ct: Token);
		var (allowed, page) = await app.Users.Of(target).ScopeTo(staff).Sessions.ListAsync(new PageRequest(0, 10),
			ct: Token);

		Assert.Equal(UserOutcome.Denied, denied);
		Assert.Null(none);
		Assert.True(allowed == UserOutcome.Ok);
		Assert.Single(page!.Items);
		Assert.Single(app.ModerationEntries, e => e.Action == ModerationAction.ViewLogins);
	}

	[Fact]
	public async Task OpenIrc_KeyForSession()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("KeyOwner", "password");
		var api = await app.LoginAsync(user);

		var (key, session) = await OpenIrcAsync(app, api, "osu-ahr VPS");
		var result = await OpenWithTokenAsync(app, SessionType.Irc, key);

		Assert.Equal("osu-ahr VPS", session.Value.Fingerprint.ClientInfo);
		Assert.Equal(SessionType.Irc, session.Value.Type);
		Assert.True(result.Succeeded);
		Assert.Equal(session.Id, result.Connection!.Session.Id);
		Assert.IsType<IrcConnection>(result.Connection);
	}

	[Fact]
	public async Task IrcKeySurvives_WrongKeysNone()
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
	public async Task ForAnotherUser_IsRefused()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPrivilegesExtensions.All));
		var user = await app.CreateUserAsync("Other", "password");

		var (result, key, _) = await app.Users.Of(user).ScopeTo(admin).Sessions.OpenIrcAsync("sneaky");

		Assert.Equal(UserOutcome.NotSelf, result);
		Assert.Null(key);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task WithoutALabel_IsRefused(string label)
	{
		using var app = new TestApplication();
		var api = await app.LoginAsync(await app.CreateUserAsync("Unlabeled", "password"));

		var (result, _, _) = await app.Users.Of(api.Record).ScopeTo(api).Sessions.OpenIrcAsync(label);

		Assert.Equal(UserOutcome.InvalidLabel, result);
	}

	[Fact]
	public async Task TooLongLabel_Refused()
	{
		using var app = new TestApplication();
		var api = await app.LoginAsync(await app.CreateUserAsync("LongLabel", "password"));

		var (result, _, _) = await app.Users.Of(api.Record).ScopeTo(api).Sessions
			.OpenIrcAsync(new string('x', 65));

		Assert.Equal(UserOutcome.InvalidLabel, result);
	}

	[Fact]
	public async Task BasilBot_NoCloseOpen()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPrivilegesExtensions.All));
		var bot = await app.SeedBotAsync();
		await app.Users.ConnectBotAsync(Token);
		var scoped = app.Users.Of(bot).ScopeTo(admin).Sessions;

		Assert.Equal(UserOutcome.SystemAccount, (await scoped.CloseAsync(long.MaxValue)));
		Assert.Equal(UserOutcome.SystemAccount, (await scoped.CloseAllAsync(false)));
		Assert.Equal(UserOutcome.SystemAccount, (await scoped.OpenIrcAsync("x")).Result);
		Assert.All(app.Users.Online[bot].Connections, connection => Assert.True(connection.IsOpen));
	}
}