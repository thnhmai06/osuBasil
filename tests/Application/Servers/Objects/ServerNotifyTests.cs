using Basil.Application.Servers.Capabilities;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Servers.Tests.Objects;

/// <summary>
///     Behaviour of notifications to osu! clients, from the XML contracts of <see cref="Server" /> and
///     <see cref="ScopedServer" />: the server's own operation needs no privileges and reaches only open osu! clients; the
///     scoped one needs <see cref="UserPrivileges.Moderator" />.
/// </summary>
public sealed class ServerNotifyTests
{
	private const string Text = "Server restarts in 5 minutes";

	private static UserNotificationSent[] Notifications(TestApplication app)
	{
		return [.. app.UserEvents.Events.OfType<UserNotificationSent>()];
	}

	private static async Task<(UserRecord User, StableConnection Client)> OnlineClientAsync(TestApplication app,
		string name)
	{
		var user = await app.CreateUserAsync(name, "password");
		var client = Assert.IsType<StableConnection>(await app.LoginAsync(user, SessionType.Stable));
		return (user, client);
	}

	private static async Task<Connection> AnnouncerAsync(TestApplication app, UserPrivileges privileges)
	{
		var user = await app.CreateUserAsync("Announcer", "password", privileges);
		return await app.LoginAsync(user);
	}

	#region Server.Notify and NotifyAll (system operation)

	[Fact]
	public async Task OneUserOpen_ReachesOnlyClient()
	{
		using var app = new TestApplication();
		var (target, targetClient) = await OnlineClientAsync(app, "Target");
		await OnlineClientAsync(app, "Bystander");

		var sent = app.Server.Notify(Text, target);

		Assert.Equal(1, sent);
		var notification = Assert.Single(Notifications(app));
		Assert.Equal(Text, notification.Text);
		Assert.Equal([targetClient], notification.Recipients);
	}

	[Fact]
	public async Task SeveralUsers_EachCounted()
	{
		using var app = new TestApplication();
		var (first, firstClient) = await OnlineClientAsync(app, "First");
		var (second, secondClient) = await OnlineClientAsync(app, "Second");
		await OnlineClientAsync(app, "Third");

		var sent = app.Server.Notify(Text, [first, second]);

		Assert.Equal(2, sent);
		var notification = Assert.Single(Notifications(app));
		Assert.Equal(2, notification.Recipients.Count);
		Assert.Contains(firstClient, notification.Recipients);
		Assert.Contains(secondClient, notification.Recipients);
	}

	[Fact]
	public async Task SeveralWithOffline_CountsOnline()
	{
		using var app = new TestApplication();
		var (online, onlineClient) = await OnlineClientAsync(app, "Online");
		var offline = await app.CreateUserAsync("Offline", "password");

		var sent = app.Server.Notify(Text, [online, offline]);

		Assert.Equal(1, sent);
		Assert.Equal([onlineClient], Assert.Single(Notifications(app)).Recipients);
	}

	[Fact]
	public async Task OfflineUser_SendsNothing()
	{
		using var app = new TestApplication();
		var offline = await app.CreateUserAsync("Offline", "password");

		var sent = app.Server.Notify(Text, offline);

		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task NoUsersGiven_SendsNothing()
	{
		using var app = new TestApplication();
		await OnlineClientAsync(app, "Bystander");

		var sent = app.Server.Notify(Text, Array.Empty<UserRecord>());

		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Theory]
	[InlineData(SessionType.Irc)]
	[InlineData(SessionType.Api)]
	[InlineData(SessionType.StableTourney)]
	public async Task NonGameOnly_SendsNothing(SessionType type)
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync($"Only{(int)type}", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament);
		await app.LoginAsync(user, type);

		var sent = app.Server.Notify(Text, user);

		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task GameAndOthers_ReachesGameClient()
	{
		using var app = new TestApplication();
		var (user, client) = await OnlineClientAsync(app, "Many");
		await app.LoginAsync(user, SessionType.Irc);
		await app.LoginAsync(user);

		var sent = app.Server.Notify(Text, user);

		Assert.Equal(1, sent);
		Assert.Equal([client], Assert.Single(Notifications(app)).Recipients);
	}

	[Fact]
	public async Task ClosedGameClient_SendsNothing()
	{
		using var app = new TestApplication();
		var (user, client) = await OnlineClientAsync(app, "Gone");
		app.Clock.Advance(TimeSpan.FromSeconds(5));
		app.Users.Close(client, SessionEndReason.LoggedOut);
		Assert.False(client.IsOpen);

		var sent = app.Server.Notify(Text, user);

		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task NotifyEmpty_SendsNothing()
	{
		using var app = new TestApplication();
		var (user, _) = await OnlineClientAsync(app, "Target");

		var sent = app.Server.Notify("", user);

		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Theory]
	[InlineData("   ")]
	[InlineData("\t\n")]
	public async Task WhitespaceText_SentAsIs(string text)
	{
		using var app = new TestApplication();
		var (user, client) = await OnlineClientAsync(app, "Target");

		var sent = app.Server.Notify(text, user);

		Assert.Equal(1, sent);
		var notification = Assert.Single(Notifications(app));
		Assert.Equal(text, notification.Text);
		Assert.Equal([client], notification.Recipients);
	}

	[Fact]
	public async Task EveryOpenClient_ReachedAndCounted()
	{
		using var app = new TestApplication();
		var (_, first) = await OnlineClientAsync(app, "First");
		var (_, second) = await OnlineClientAsync(app, "Second");
		var (_, third) = await OnlineClientAsync(app, "Third");

		var sent = app.Server.NotifyAll(Text);

		Assert.Equal(3, sent);
		var notification = Assert.Single(Notifications(app));
		Assert.Equal(Text, notification.Text);
		Assert.Equal(3, notification.Recipients.Count);
		Assert.All(new[] { first, second, third }, client => Assert.Contains(client, notification.Recipients));
	}

	[Fact]
	public async Task ClosedNonGame_Skipped()
	{
		using var app = new TestApplication();
		var (_, open) = await OnlineClientAsync(app, "Open");
		var (_, closed) = await OnlineClientAsync(app, "Closed");
		app.Clock.Advance(TimeSpan.FromSeconds(5));
		app.Users.Close(closed, SessionEndReason.LoggedOut);
		await app.LoginAsync(await app.CreateUserAsync("Chatter", "password"), SessionType.Irc);
		await app.LoginAsync(await app.CreateUserAsync("Integrator", "password"));
		await app.LoginAsync(await app.CreateUserAsync("Referee", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament), SessionType.StableTourney);

		var sent = app.Server.NotifyAll(Text);

		Assert.Equal(1, sent);
		Assert.Equal([open], Assert.Single(Notifications(app)).Recipients);
	}

	[Fact]
	public void NoClientsOnline_SendsNothing()
	{
		using var app = new TestApplication();

		var sent = app.Server.NotifyAll(Text);

		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task NotifyAllEmpty_SendsNothing()
	{
		using var app = new TestApplication();
		await OnlineClientAsync(app, "Target");

		var sent = app.Server.NotifyAll("");

		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task AllWhitespaceText_SentAsIs()
	{
		using var app = new TestApplication();
		var (_, client) = await OnlineClientAsync(app, "Target");

		var sent = app.Server.NotifyAll("  ");

		Assert.Equal(1, sent);
		var notification = Assert.Single(Notifications(app));
		Assert.Equal("  ", notification.Text);
		Assert.Equal([client], notification.Recipients);
	}

	#endregion

	#region ScopedServer.Notify and NotifyAll (needs ChatManageAnnounce)

	[Fact]
	public async Task OneUserAnnounce_OkReachesClient()
	{
		using var app = new TestApplication();
		var actor = await AnnouncerAsync(app, UserPrivilegesExtensions.Default | UserPrivileges.Moderator);
		var (target, client) = await OnlineClientAsync(app, "Target");

		var (result, sent) = app.Server.ScopeTo(actor).Notify(Text, target);

		Assert.Equal(ServerOutcome.Ok, result);
		Assert.Equal(1, sent);
		Assert.Equal([client], Assert.Single(Notifications(app)).Recipients);
	}

	[Fact]
	public async Task SeveralAnnounce_OkCountsClients()
	{
		using var app = new TestApplication();
		var actor = await AnnouncerAsync(app, UserPrivilegesExtensions.Default | UserPrivileges.Moderator);
		var (first, _) = await OnlineClientAsync(app, "First");
		var (second, _) = await OnlineClientAsync(app, "Second");

		var (result, sent) = app.Server.ScopeTo(actor).Notify(Text, [first.Id, second.Id]);

		Assert.Equal(ServerOutcome.Ok, result);
		Assert.Equal(2, sent);
	}

	[Fact]
	public async Task AllAnnounce_OkCountsEvery()
	{
		using var app = new TestApplication();
		var actor = await AnnouncerAsync(app, UserPrivilegesExtensions.Default | UserPrivileges.Moderator);
		await OnlineClientAsync(app, "First");
		await OnlineClientAsync(app, "Second");

		var (result, sent) = app.Server.ScopeTo(actor).NotifyAll(Text);

		Assert.Equal(ServerOutcome.Ok, result);
		Assert.Equal(2, sent);
	}

	[Fact]
	public async Task EmptyAnnounce_OkSendsNothing()
	{
		using var app = new TestApplication();
		var actor = await AnnouncerAsync(app, UserPrivilegesExtensions.Default | UserPrivileges.Moderator);
		await OnlineClientAsync(app, "Target");

		var (result, sent) = app.Server.ScopeTo(actor).NotifyAll("");

		Assert.True(result == ServerOutcome.Ok);
		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task OneNoAnnounce_DeniedSendsNothing()
	{
		using var app = new TestApplication();
		var actor = await AnnouncerAsync(app, UserPrivilegesExtensions.Default & ~UserPrivileges.Moderator);
		var (target, _) = await OnlineClientAsync(app, "Target");

		var (result, sent) = app.Server.ScopeTo(actor).Notify(Text, target);

		Assert.False(result == ServerOutcome.Ok);
		Assert.Equal(ServerOutcome.Denied, result);
		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task SeveralNoAnnounce_DeniedSendsNothing()
	{
		using var app = new TestApplication();
		var actor = await AnnouncerAsync(app, UserPrivilegesExtensions.Default & ~UserPrivileges.Moderator);
		var (first, _) = await OnlineClientAsync(app, "First");
		var (second, _) = await OnlineClientAsync(app, "Second");

		var (result, sent) = app.Server.ScopeTo(actor).Notify(Text, [first.Id, second.Id]);

		Assert.Equal(ServerOutcome.Denied, result);
		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task AllNoAnnounce_DeniedSendsNothing()
	{
		using var app = new TestApplication();
		var actor = await AnnouncerAsync(app, UserPrivilegesExtensions.Default & ~UserPrivileges.Moderator);
		await OnlineClientAsync(app, "Target");

		var (result, sent) = app.Server.ScopeTo(actor).NotifyAll(Text);

		Assert.Equal(ServerOutcome.Denied, result);
		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	#endregion
}