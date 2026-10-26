using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Sessions;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Application.Runtime.Tests.Multiplayer;

/// <summary>
///     What a closed connection leaves behind in the multiplayer lobby: its room and its place among the lobby's
///     watchers.
/// </summary>
public sealed class ConnectionLobbyHandlerTests
{
	[Fact]
	public async Task Handle_UserConnectionClosed_ReleasesTheRoomItPlayedIn()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var handler = Handler(app);
		app.Users.Close(setup.Player, ConnectionCloseReason.ConnectionLost);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => @event.Connection == setup.Player);
		Assert.NotNull(setup.Room.Slots.Find(setup.Player));

		await handler.Handle(closed, TestContext.Current.CancellationToken);

		Assert.Null(setup.Room.Slots.Find(setup.Player));
		Assert.Single(app.RoomEvents.Events.OfType<RoomPlayerLeft>(), left => left.Player == setup.Player);
	}

	[Fact]
	public async Task Handle_UserConnectionClosed_StopsWatchingTheLobby()
	{
		using var app = new TestApplication();
		var handler = Handler(app);
		var player = await RoomFixture.PlayerAsync(app, "Watcher");
		app.Lobby.ScopeTo(player).Watch();
		Assert.Contains(player, app.Lobby.Watchers);
		app.Users.Close(player, ConnectionCloseReason.ConnectionLost);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => @event.Connection == player);

		await handler.Handle(closed, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(player, app.Lobby.Watchers);
		Assert.Single(app.LobbyEvents.Events.OfType<LobbyWatcherLeft>(), left => left.Watcher == player);
	}

	private static ConnectionLobbyHandler Handler(TestApplication app)
	{
		return new ConnectionLobbyHandler(app.Lobby, NullLogger<ConnectionLobbyHandler>.Instance);
	}
}