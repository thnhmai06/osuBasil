using System.Net;
using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Domain.Authentication;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Registry;

/// <summary>
///     The trail of connections of a session: one entry for each time a client was connected, written when it connects
///     and when it leaves, continued when the same client comes back soon from the same address, and ended by the next
///     start when the server stopped with it open.
/// </summary>
public sealed class ConnectionTrailTests
{
	private static readonly IPAddress Elsewhere = IPAddress.Parse("192.0.2.77");

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static Task<LoginResult> OpenAsync(TestApplication app, string token, IPAddress ip)
	{
		return app.Users.LoginWithTokenAsync(SessionType.Api, token, ip, ct: Token);
	}

	private static async Task LetGoAsync(TestApplication app, TimeSpan idleFor)
	{
		app.Clock.Advance(idleFor);
		Assert.True(app.Users.CloseIdle() > 0);
		await Task.Yield();
	}

	[Fact]
	public async Task Login_RecordsSessionConnection()
	{
		using var app = new TestApplication();
		var connection = await app.LoginAsync(await app.CreateUserAsync("Trail", "password"));

		var entry = Assert.Single(app.StoredConnections);
		Assert.Equal(connection.Session.Id, entry.Value.SessionId);
		Assert.Equal(IPAddress.Loopback, entry.Value.Ip);
		Assert.Equal(app.Clock.GetUtcNow(), entry.Value.OpenedAt);
		Assert.Null(entry.Value.ClosedAt);
		Assert.Null(entry.Value.CloseReason);
	}

	[Theory]
	[InlineData(SessionType.Api)]
	[InlineData(SessionType.Stable)]
	[InlineData(SessionType.StableTourney)]
	public async Task PasswordLogins_RecordConnection(SessionType type)
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Kinds", "password", UserPrivilegesExtensions.All);

		var connection = await app.LoginAsync(user, type);

		Assert.Equal(connection.Session.Id, Assert.Single(app.StoredConnections).Value.SessionId);
	}

	[Fact]
	public async Task IrcKeyOpened_RecordsConnection()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Key", "password");
		var api = await app.LoginAsync(user);
		var (result, key, session) = await app.Users.Of(user).ScopeTo(api).Sessions.OpenIrcAsync("laptop");
		Assert.True(result.IsOk);

		var irc = await app.Users.LoginWithTokenAsync(SessionType.Irc, key!, IPAddress.Loopback,
			ct: Token);

		Assert.True(irc.Succeeded);
		Assert.Single(app.StoredConnections, entry => entry.Value.SessionId == session!.Id);
	}

	[Fact]
	public async Task RefusedLogin_NoConnection()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Refused", "password");
		var wrong = await app.AttemptLoginAsync(user.Value.Name, "wrong");

		Assert.False(wrong.Succeeded);
		Assert.Empty(app.StoredConnections);
	}

	[Fact]
	public async Task Closing_StoresEndActivity()
	{
		using var app = new TestApplication();
		var connection = await app.LoginAsync(await app.CreateUserAsync("Leaves", "password"));
		app.Clock.Advance(TimeSpan.FromSeconds(10));
		app.Users.MarkActive(connection);
		var lastActive = app.Clock.GetUtcNow();
		app.Clock.Advance(TimeSpan.FromSeconds(5));

		app.Users.Close(connection, SessionEndReason.LoggedOut);

		var entry = Assert.Single(app.StoredConnections);
		Assert.Equal(app.Clock.GetUtcNow(), entry.Value.ClosedAt);
		Assert.Equal(SessionEndReason.LoggedOut, entry.Value.CloseReason);
		Assert.Equal(lastActive, entry.Value.LastActiveAt);
	}

	[Fact]
	public async Task SameAddressSoon_ContinuesConnection()
	{
		using var app = new TestApplication();
		var first = await app.LoginAsync(await app.CreateUserAsync("Back", "password"));
		await LetGoAsync(app, TimeSpan.FromSeconds(301));
		app.Clock.Advance(TimeSpan.FromMinutes(2));

		var again = await OpenAsync(app, first.Token, IPAddress.Loopback);

		Assert.True(again.Succeeded);
		var entry = Assert.Single(app.StoredConnections);
		Assert.Null(entry.Value.ClosedAt);
		Assert.Null(entry.Value.CloseReason);
	}

	[Fact]
	public async Task AnotherAddress_AddsConnection()
	{
		using var app = new TestApplication();
		var first = await app.LoginAsync(await app.CreateUserAsync("Moves", "password"));
		await LetGoAsync(app, TimeSpan.FromSeconds(301));

		await OpenAsync(app, first.Token, Elsewhere);

		Assert.Equal(2, app.StoredConnections.Count);
		Assert.Equal([IPAddress.Loopback, Elsewhere], app.StoredConnections.Select(entry => entry.Value.Ip));
	}

	[Fact]
	public async Task AfterWindow_AddsConnection()
	{
		using var app = new TestApplication();
		var first = await app.LoginAsync(await app.CreateUserAsync("Late", "password"));
		await LetGoAsync(app, TimeSpan.FromSeconds(301));
		app.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

		await OpenAsync(app, first.Token, IPAddress.Loopback);

		Assert.Equal(2, app.StoredConnections.Count);
		Assert.NotNull(app.StoredConnections[0].Value.ClosedAt);
		Assert.Null(app.StoredConnections[1].Value.ClosedAt);
	}

	[Fact]
	public async Task AtWindowEnd_Continues()
	{
		using var app = new TestApplication();
		var first = await app.LoginAsync(await app.CreateUserAsync("Edge", "password"));
		await LetGoAsync(app, TimeSpan.FromSeconds(301));
		var closedAt = app.StoredConnections[0].Value.ClosedAt!.Value;
		app.Clock.Advance(closedAt + TimeSpan.FromMinutes(5) - app.Clock.GetUtcNow());

		await OpenAsync(app, first.Token, IPAddress.Loopback);

		Assert.Single(app.StoredConnections);
	}

	[Fact]
	public async Task TwoSessions_ConnectionsApart()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Two", "password");
		var one = await app.LoginAsync(user);
		var two = await app.LoginAsync(user);

		app.Users.Close(one, SessionEndReason.LoggedOut);

		Assert.NotNull(app.StoredConnections.Single(entry => entry.Value.SessionId == one.Session.Id).Value.ClosedAt);
		Assert.Null(app.StoredConnections.Single(entry => entry.Value.SessionId == two.Session.Id).Value.ClosedAt);
	}

	[Fact]
	public async Task BasilBotsConnections_LeaveNoTrail()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();

		await app.Server.RecoverAsync(Token);

		Assert.Empty(app.StoredConnections);
	}

	[Fact]
	public async Task FlushedActivity_StoresLastUse()
	{
		using var app = new TestApplication();
		var connection = await app.LoginAsync(await app.CreateUserAsync("Flush", "password"));
		app.Clock.Advance(TimeSpan.FromSeconds(30));
		app.Users.MarkActive(connection);

		await app.Users.FlushActivityAsync();

		Assert.Equal(app.Clock.GetUtcNow(), Assert.Single(app.StoredConnections).Value.LastActiveAt);
	}

	[Fact]
	public async Task StopClosesOpen_EndsOsuOnly()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Crash", "password");
		var api = await app.LoginAsync(user);
		var osu = await app.LoginAsync(user, SessionType.Stable);
		app.Clock.Advance(TimeSpan.FromSeconds(20));
		app.Users.MarkActive(api);
		app.Users.MarkActive(osu);
		await app.Users.FlushActivityAsync();
		var lastActive = app.Clock.GetUtcNow();
		app.Clock.Advance(TimeSpan.FromHours(3));

		var closed = await app.Users.CloseStoppedAsync();

		Assert.Equal(2, closed);
		Assert.All(app.StoredConnections, entry =>
		{
			Assert.Equal(lastActive, entry.Value.ClosedAt);
			Assert.Equal(SessionEndReason.ServerStopped, entry.Value.CloseReason);
		});
		var osuSession = app.StoredSessions.Single(entry => entry.Id == osu.Session.Id);
		Assert.Equal(new SessionEnd(lastActive, SessionEndReason.ServerStopped), osuSession.Value.End);
		Assert.Null(app.StoredSessions.Single(entry => entry.Id == api.Session.Id).Value.End);
	}

	[Fact]
	public async Task AlreadyClosed_LeftUntouched()
	{
		using var app = new TestApplication();
		var connection = await app.LoginAsync(await app.CreateUserAsync("Already", "password"));
		app.Users.Close(connection, SessionEndReason.LoggedOut);

		var closed = await app.Users.CloseStoppedAsync();

		Assert.Equal(0, closed);
		Assert.Equal(SessionEndReason.LoggedOut, Assert.Single(app.StoredConnections).Value.CloseReason);
	}

	[Fact]
	public async Task ListOwnSession_NewestFirst()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Reads", "password");
		var first = await app.LoginAsync(user);
		await LetGoAsync(app, TimeSpan.FromSeconds(301));
		await OpenAsync(app, first.Token, Elsewhere);
		var reader = await app.LoginAsync(user);

		var (result, page) = await app.Users.Of(user).ScopeTo(reader).Sessions
			.ListConnectionsAsync(first.Session.Id, new PageRequest(0, 10), Token);

		Assert.True(result.IsOk);
		Assert.Equal([Elsewhere, IPAddress.Loopback], page!.Items.Select(entry => entry.Value.Ip));
		Assert.False(page.HasNext);
	}

	[Fact]
	public async Task ListOtherAccount_NeedsViewRecorded()
	{
		using var app = new TestApplication();
		var target = await app.CreateUserAsync("Watched", "password");
		var session = await app.LoginAsync(target);
		var nosy = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var staff = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPrivilegesExtensions.All));

		var (denied, none) = await app.Users.Of(target).ScopeTo(nosy).Sessions
			.ListConnectionsAsync(session.Session.Id, new PageRequest(0, 10), Token);
		Assert.Empty(app.ModerationEntries);
		var (allowed, page) = await app.Users.Of(target).ScopeTo(staff).Sessions
			.ListConnectionsAsync(session.Session.Id, new PageRequest(0, 10), Token);

		Assert.Equal(UserPrivileges.Moderator, denied.Missing);
		Assert.Null(none);
		Assert.True(allowed.IsOk);
		Assert.Single(page!.Items);
		Assert.Single(app.ModerationEntries, entry => entry.Action == ModerationAction.ViewLogins);
	}

	[Fact]
	public async Task ForeignSession_NotFound()
	{
		using var app = new TestApplication();
		var mine = await app.LoginAsync(await app.CreateUserAsync("Mine", "password"));
		var theirs = await app.LoginAsync(await app.CreateUserAsync("Theirs", "password"));

		var (result, page) = await app.Users.Of(mine.Record).ScopeTo(mine).Sessions
			.ListConnectionsAsync(theirs.Session.Id, new PageRequest(0, 10), Token);

		Assert.Equal(UserOutcome.NotFound, result.Outcome);
		Assert.Null(page);
	}

	[Fact]
	public async Task ListConnections_PagesTheTrail()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Pages", "password");
		var first = await app.LoginAsync(user);
		for (var i = 0; i < 2; i++)
		{
			await LetGoAsync(app, TimeSpan.FromSeconds(301));
			await OpenAsync(app, first.Token, i == 0 ? Elsewhere : IPAddress.Loopback);
		}

		var scoped = app.Users.Of(user).ScopeTo(first).Sessions;
		var (_, head) = await scoped.ListConnectionsAsync(first.Session.Id, new PageRequest(0, 2), Token);
		var (_, tail) = await scoped.ListConnectionsAsync(first.Session.Id, new PageRequest(2, 2), Token);

		Assert.Equal(2, head!.Items.Count);
		Assert.True(head.HasNext);
		Assert.Single(tail!.Items);
		Assert.False(tail.HasNext);
	}
}
