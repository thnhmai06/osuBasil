using Basil.Application.Runtime.Chat;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Sessions;
using Basil.Testing;
using Basil.Domain.Chat;
using Basil.Domain.Permissions;
using Basil.Infrastructure.Runtime.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Runtime.Tests.Sessions;

/// <summary>
///     What happens around a connection without the transport asking for it: a new connection joins the channels that
///     join themselves, and a closed one leaves its room, its channels and the lobby watch behind.
/// </summary>
public sealed class ConnectionHandlerTests
{
	[Fact]
	public async Task HandleAsync_UserConnectionOpened_JoinsTheChannelsThatJoinThemselves()
	{
		using var app = new TestApplication();
		var auto = await OpenChannelAsync(app, "#auto", autoJoin: true);
		var manual = await OpenChannelAsync(app, "#manual");
		var handler = Handler(app);
		var player = await RoomFixture.PlayerAsync(app, "Joiner");
		var opened = Assert.Single(app.UserEvents.Events.OfType<UserConnectionOpened>(),
			@event => @event.Connection == player);

		await handler.HandleAsync(opened, TestContext.Current.CancellationToken);

		Assert.Contains(player, auto.Members);
		Assert.DoesNotContain(player, manual.Members);
	}

	[Fact]
	public async Task HandleAsync_UserConnectionClosed_ReleasesTheRoomItPlayedIn()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var handler = Handler(app);
		app.Users.Close(setup.Player, ConnectionCloseReason.ConnectionLost);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => @event.Connection == setup.Player);
		Assert.NotNull(setup.Room.Slots.Find(setup.Player));

		await handler.HandleAsync(closed, TestContext.Current.CancellationToken);

		Assert.Null(setup.Room.Slots.Find(setup.Player));
		Assert.Single(app.RoomEvents.Events.OfType<RoomPlayerLeft>(), left => left.Player == setup.Player);
	}

	[Fact]
	public async Task HandleAsync_UserConnectionClosed_PartsItsChannels()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, "#lobby");
		var handler = Handler(app);
		var player = await RoomFixture.PlayerAsync(app, "Chatter");
		Assert.True(channel.ScopeTo(player).Join().IsOk);
		app.Users.Close(player, ConnectionCloseReason.ConnectionLost);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => @event.Connection == player);

		await handler.HandleAsync(closed, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(player, channel.Members);
	}

	[Fact]
	public async Task HandleAsync_UserConnectionClosed_StopsWatchingTheLobby()
	{
		using var app = new TestApplication();
		var handler = Handler(app);
		var player = await RoomFixture.PlayerAsync(app, "Watcher");
		app.Lobby.ScopeTo(player).Watch();
		Assert.Contains(player, app.Lobby.Watchers);
		app.Users.Close(player, ConnectionCloseReason.ConnectionLost);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => @event.Connection == player);

		await handler.HandleAsync(closed, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(player, app.Lobby.Watchers);
		Assert.Single(app.LobbyEvents.Events.OfType<LobbyWatcherLeft>(), left => left.Watcher == player);
	}

	private static ConnectionHandler Handler(TestApplication app)
	{
		return new ConnectionHandler(app.Channels, app.Lobby, app.Users, NullLogger<ConnectionHandler>.Instance);
	}

	/// <summary>Opens the channel a standard account makes for itself, with no read or write role required.</summary>
	/// <param name="app">The application the channel lives in.</param>
	/// <param name="name">The channel name, with its leading <c>#</c>.</param>
	/// <param name="autoJoin">Whether a connection joins the channel at login.</param>
	/// <returns>The open channel.</returns>
	private static async Task<GeneralChannel> OpenChannelAsync(TestApplication app, string name, bool autoJoin = false)
	{
		var admin = await app.CreateUserAsync($"Admin{name.TrimStart('#')}", "password", PermissionsGroups.All);
		var by = await app.LoginAsync(admin);
		var record = new GeneralChannelRecord
		{
			Name = name,
			Value = new GeneralChannelData { AutoJoin = autoJoin }
		};
		Assert.True((await app.Server.ScopeTo(by).Channels.CreateAsync(record,
			TestContext.Current.CancellationToken)).IsOk);
		return app.Channels.Find(name)!;
	}
}
