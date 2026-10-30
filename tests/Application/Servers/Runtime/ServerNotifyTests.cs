using Basil.Application.Users.Connections;
using Basil.Application.Servers.Runtime;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Servers.Tests.Runtime;

/// <summary>
///     Behaviour of notifications to osu! clients, from the XML contracts of <see cref="Server" /> and
///     <see cref="ScopedServer" />: the server's own operation needs no rights and reaches only open osu! clients; the
///     scoped one needs <see cref="UserPermissions.ChatManageAnnounce" />.
/// </summary>
public sealed class ServerNotifyTests
{
	private const string Text = "Server restarts in 5 minutes";

	private static UserNotificationSent[] Notifications(TestApplication app)
	{
		return [.. app.UserEvents.Events.OfType<UserNotificationSent>()];
	}

	private static async Task<(UserRecord User, BanchoConnection Client)> OnlineClientAsync(TestApplication app,
		string name)
	{
		var user = await app.CreateUserAsync(name, "password");
		var client = Assert.IsType<BanchoConnection>(await app.LoginAsync(user, SessionType.Osu));
		return (user, client);
	}

	private static async Task<Connection> AnnouncerAsync(TestApplication app, UserPermissions permissions)
	{
		var user = await app.CreateUserAsync("Announcer", "password", permissions);
		return await app.LoginAsync(user);
	}

	#region Server.Notify and NotifyAll (system operation)

	[Fact]
	public async Task Notify_OneUserWithOpenClient_ReachesOnlyThatClient()
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
	public async Task Notify_SeveralUsers_ReachesEachOpenClientAndReturnsTheCount()
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
	public async Task Notify_SeveralUsersOfWhichOneIsOffline_CountsOnlyTheOnlineOne()
	{
		using var app = new TestApplication();
		var (online, onlineClient) = await OnlineClientAsync(app, "Online");
		var offline = await app.CreateUserAsync("Offline", "password");

		var sent = app.Server.Notify(Text, [online, offline]);

		Assert.Equal(1, sent);
		Assert.Equal([onlineClient], Assert.Single(Notifications(app)).Recipients);
	}

	[Fact]
	public async Task Notify_OfflineUser_SendsNothing()
	{
		using var app = new TestApplication();
		var offline = await app.CreateUserAsync("Offline", "password");

		var sent = app.Server.Notify(Text, offline);

		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task Notify_NoUsersGiven_SendsNothing()
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
	[InlineData(SessionType.OsuTourney)]
	public async Task Notify_UserWhoseOnlyConnectionIsNotAnOsuGameClient_SendsNothing(SessionType type)
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync($"Only{type}", "password",
			UserPermissionsGroups.Default | UserPermissions.RoomObserve);
		await app.LoginAsync(user, type);

		var sent = app.Server.Notify(Text, user);

		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task Notify_UserWithGameClientAndOtherConnections_ReachesOnlyTheGameClient()
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
	public async Task Notify_UserWhoseGameClientClosed_SendsNothing()
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
	public async Task Notify_EmptyText_SendsNothing()
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
	public async Task Notify_WhitespaceOnlyText_IsSentAsIs(string text)
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
	public async Task NotifyAll_EveryOpenGameClient_IsReachedAndCounted()
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
	public async Task NotifyAll_SkipsClosedClientsAndConnectionsThatAreNotGameClients()
	{
		using var app = new TestApplication();
		var (_, open) = await OnlineClientAsync(app, "Open");
		var (_, closed) = await OnlineClientAsync(app, "Closed");
		app.Clock.Advance(TimeSpan.FromSeconds(5));
		app.Users.Close(closed, SessionEndReason.LoggedOut);
		await app.LoginAsync(await app.CreateUserAsync("Chatter", "password"), SessionType.Irc);
		await app.LoginAsync(await app.CreateUserAsync("Integrator", "password"));
		await app.LoginAsync(await app.CreateUserAsync("Referee", "password",
			UserPermissionsGroups.Default | UserPermissions.RoomObserve), SessionType.OsuTourney);

		var sent = app.Server.NotifyAll(Text);

		Assert.Equal(1, sent);
		Assert.Equal([open], Assert.Single(Notifications(app)).Recipients);
	}

	[Fact]
	public void NotifyAll_NoClientsOnline_SendsNothing()
	{
		using var app = new TestApplication();

		var sent = app.Server.NotifyAll(Text);

		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task NotifyAll_EmptyText_SendsNothing()
	{
		using var app = new TestApplication();
		await OnlineClientAsync(app, "Target");

		var sent = app.Server.NotifyAll("");

		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task NotifyAll_WhitespaceOnlyText_IsSentAsIs()
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
	public async Task ScopedNotify_OneUserWithChatAnnounce_IsOkAndReachesTheClient()
	{
		using var app = new TestApplication();
		var by = await AnnouncerAsync(app, UserPermissionsGroups.Default | UserPermissions.ChatManageAnnounce);
		var (target, client) = await OnlineClientAsync(app, "Target");

		var (result, sent) = app.Server.ScopeTo(by).Notify(Text, target);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		Assert.Equal(1, sent);
		Assert.Equal([client], Assert.Single(Notifications(app)).Recipients);
	}

	[Fact]
	public async Task ScopedNotify_SeveralUsersWithChatAnnounce_IsOkAndCountsClients()
	{
		using var app = new TestApplication();
		var by = await AnnouncerAsync(app, UserPermissionsGroups.Default | UserPermissions.ChatManageAnnounce);
		var (first, _) = await OnlineClientAsync(app, "First");
		var (second, _) = await OnlineClientAsync(app, "Second");

		var (result, sent) = app.Server.ScopeTo(by).Notify(Text, [first, second]);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		Assert.Equal(2, sent);
	}

	[Fact]
	public async Task ScopedNotifyAll_WithChatAnnounce_IsOkAndCountsEveryClient()
	{
		using var app = new TestApplication();
		var by = await AnnouncerAsync(app, UserPermissionsGroups.Default | UserPermissions.ChatManageAnnounce);
		await OnlineClientAsync(app, "First");
		await OnlineClientAsync(app, "Second");

		var (result, sent) = app.Server.ScopeTo(by).NotifyAll(Text);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		Assert.Equal(2, sent);
	}

	[Fact]
	public async Task ScopedNotifyAll_EmptyTextWithChatAnnounce_IsOkAndSendsNothing()
	{
		using var app = new TestApplication();
		var by = await AnnouncerAsync(app, UserPermissionsGroups.Default | UserPermissions.ChatManageAnnounce);
		await OnlineClientAsync(app, "Target");

		var (result, sent) = app.Server.ScopeTo(by).NotifyAll("");

		Assert.True(result.IsOk);
		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task ScopedNotify_OneUserWithoutChatAnnounce_IsDeniedNamingItAndSendsNothing()
	{
		using var app = new TestApplication();
		var by = await AnnouncerAsync(app, UserPermissionsGroups.Default & ~UserPermissions.ChatManageAnnounce);
		var (target, _) = await OnlineClientAsync(app, "Target");

		var (result, sent) = app.Server.ScopeTo(by).Notify(Text, target);

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.ChatManageAnnounce, result.Missing);
		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task ScopedNotify_SeveralUsersWithoutChatAnnounce_IsDeniedNamingItAndSendsNothing()
	{
		using var app = new TestApplication();
		var by = await AnnouncerAsync(app, UserPermissionsGroups.Default & ~UserPermissions.ChatManageAnnounce);
		var (first, _) = await OnlineClientAsync(app, "First");
		var (second, _) = await OnlineClientAsync(app, "Second");

		var (result, sent) = app.Server.ScopeTo(by).Notify(Text, [first, second]);

		Assert.Equal(UserPermissions.ChatManageAnnounce, result.Missing);
		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task ScopedNotifyAll_WithoutChatAnnounce_IsDeniedNamingItAndSendsNothing()
	{
		using var app = new TestApplication();
		var by = await AnnouncerAsync(app, UserPermissionsGroups.Default & ~UserPermissions.ChatManageAnnounce);
		await OnlineClientAsync(app, "Target");

		var (result, sent) = app.Server.ScopeTo(by).NotifyAll(Text);

		Assert.Equal(UserPermissions.ChatManageAnnounce, result.Missing);
		Assert.Equal(0, sent);
		Assert.Empty(Notifications(app));
	}

	[Fact]
	public async Task ScopedNotifyAll_ChatAnnounceSuspendedByRestriction_IsDenied()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Muted", "password",
			UserPermissionsGroups.Default | UserPermissions.ChatManageAnnounce);
		app.AddRestriction(user, UserPermissions.ChatManageAnnounce);
		var by = await app.LoginAsync(user);
		await OnlineClientAsync(app, "Target");

		var (result, sent) = app.Server.ScopeTo(by).NotifyAll(Text);

		Assert.Equal(UserPermissions.ChatManageAnnounce, result.Missing);
		Assert.Equal(0, sent);
	}

	#endregion
}
