using Basil.Application.Multiplayer.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Host.Bancho.Support;
using Basil.Protocol.Bancho.Wire.Packets;
using Mediator;

namespace Basil.Host.Bancho.Delivery;

/// <summary>Says who in a room and its lobby hears about changes.</summary>
internal sealed class RoomAudience(Lobby lobby, Outbox outbox)
{
	/// <summary>Gets the osu! clients in the room: its players and its tournament observers.</summary>
	public IEnumerable<Connection> Of(Room room)
	{
		return room.Slots.Select(slot => (Connection?)slot.Player).OfType<Connection>()
			.Concat(room.Members.Observers);
	}

	/// <summary>Sends packets to the room's players and observers.</summary>
	public void ToRoom(Room room, Action<PacketWriter> write)
	{
		outbox.Send(Of(room), write);
	}

	/// <summary>Sends the current state of a room to everybody who shows it.</summary>
	public void Refresh(Room room)
	{
		if (room.IsClosed) return;
		var match = BanchoView.Match(room);
		outbox.Send(room.Slots.Select(slot => (Connection?)slot.Player).OfType<Connection>(),
			writer => writer.UpdateMatch(match));
		outbox.Send(room.Members.Observers, writer => writer.UpdateMatch(match, false));
		if (!room.Settings.IsPrivate) outbox.Send(lobby.Watchers, writer => writer.UpdateMatch(match, false));
	}
}

internal abstract class RoomRefreshDelivery<TEvent>(RoomAudience audience) : INotificationHandler<TEvent>
	where TEvent : RoomEvent
{
	public ValueTask Handle(TEvent notification, CancellationToken cancellationToken)
	{
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoomSettingsDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomSettingsChanged>(audience);

internal sealed class RoomLockDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomLockChanged>(audience);

internal sealed class RoomSlotTeamDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomSlotTeamChanged>(audience);

internal sealed class RoomSlotModsDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomSlotModsChanged>(audience);

internal sealed class RoomSlotStatusDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomSlotStatusChanged>(audience);

internal sealed class RoomSlotsArrangedDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomSlotsArranged>(audience);

internal sealed class RoomPlayerMovedDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomPlayerMoved>(audience);

internal sealed class RoomPlayerLeftDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomPlayerLeft>(audience);

/// <summary>Sends the host the packet that makes it the host, then the new state.</summary>
internal sealed class RoomHostDelivery(RoomAudience audience, Outbox outbox) : INotificationHandler<RoomHostChanged>
{
	public ValueTask Handle(RoomHostChanged notification, CancellationToken cancellationToken)
	{
		if (notification.Host is { } host) outbox.Send(host, writer => writer.MatchTransferHost());
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

/// <summary>Seats a client on the match screen and refreshes everyone.</summary>
internal sealed class RoomPlayerJoinedDelivery(RoomAudience audience, Outbox outbox) : INotificationHandler<RoomPlayerJoined>
{
	public ValueTask Handle(RoomPlayerJoined notification, CancellationToken cancellationToken)
	{
		var match = BanchoView.Match(notification.Room);
		outbox.Send(notification.Player, writer => writer.MatchJoinSuccess(match));
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

/// <summary>Sends a kicked client off the match screen and refreshes everyone.</summary>
internal sealed class RoomPlayerKickedDelivery(RoomAudience audience, Outbox outbox) : INotificationHandler<RoomPlayerKicked>
{
	public ValueTask Handle(RoomPlayerKicked notification, CancellationToken cancellationToken)
	{
		outbox.Send(notification.Player, writer => writer.DisposeMatch(notification.Room.Id));
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

/// <summary>Sends a banned client off the match screen and refreshes everyone.</summary>
internal sealed class RoomPlayerBannedDelivery(RoomAudience audience, Outbox outbox) : INotificationHandler<RoomPlayerBanned>
{
	public ValueTask Handle(RoomPlayerBanned notification, CancellationToken cancellationToken)
	{
		if (notification.Evicted is { } evicted) outbox.Send(evicted, writer => writer.DisposeMatch(notification.Room.Id));
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

/// <summary>Sends an evicted client off the match screen when its slot is locked.</summary>
internal sealed class RoomSlotLockDelivery(RoomAudience audience, Outbox outbox) : INotificationHandler<RoomSlotLockChanged>
{
	public ValueTask Handle(RoomSlotLockChanged notification, CancellationToken cancellationToken)
	{
		if (notification.Evicted is { } evicted) outbox.Send(evicted, writer => writer.DisposeMatch(notification.Room.Id));
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

/// <summary>Shows the match to a tournament observer that joined.</summary>
internal sealed class RoomObserverDelivery(Outbox outbox) : INotificationHandler<RoomObserverJoined>
{
	public ValueTask Handle(RoomObserverJoined notification, CancellationToken cancellationToken)
	{
		var match = BanchoView.Match(notification.Room);
		outbox.Send(notification.Observer, writer => writer.UpdateMatch(match, false));
		return ValueTask.CompletedTask;
	}
}

/// <summary>Invites a user to a room.</summary>
internal sealed class RoomInviteDelivery(UserRegistry registry, Outbox outbox) : INotificationHandler<RoomPlayerInvited>
{
	public ValueTask Handle(RoomPlayerInvited notification, CancellationToken cancellationToken)
	{
		if (registry.OpenBanchoClient(notification.Player) is { } target)
			outbox.Send(target, writer => writer.MatchInvite(notification.By.Id, notification.By.Value.Name,
				$"Come join my game: {notification.Room.UrlEmbed}", notification.Player.Value.Name));

		return ValueTask.CompletedTask;
	}
}

/// <summary>Shows a new room to the lobby and seats the creator.</summary>
internal sealed class LobbyRoomOpenedDelivery(Lobby lobby, Outbox outbox) : INotificationHandler<LobbyRoomOpened>
{
	public ValueTask Handle(LobbyRoomOpened notification, CancellationToken cancellationToken)
	{
		var match = BanchoView.Match(notification.Room);
		if (!notification.Room.Settings.IsPrivate) outbox.Send(lobby.Watchers, writer => writer.NewMatch(match, false));
		if (notification.Host is { } host) outbox.Send(host, writer => writer.MatchJoinSuccess(match));
		return ValueTask.CompletedTask;
	}
}

/// <summary>Removes a closed room from the lobby and from the screens of its players.</summary>
internal sealed class LobbyRoomClosedDelivery(Lobby lobby, Outbox outbox) : INotificationHandler<LobbyRoomClosed>
{
	public ValueTask Handle(LobbyRoomClosed notification, CancellationToken cancellationToken)
	{
		var id = notification.Room.Id;
		outbox.Send(lobby.Watchers.Concat<Connection>(notification.Evicted), writer => writer.DisposeMatch(id));
		return ValueTask.CompletedTask;
	}
}

/// <summary>Lists the open rooms to a client that opened the lobby.</summary>
internal sealed class LobbyWatcherDelivery(Lobby lobby, Outbox outbox) : INotificationHandler<LobbyWatcherJoined>
{
	public ValueTask Handle(LobbyWatcherJoined notification, CancellationToken cancellationToken)
	{
		var matches = lobby.Rooms.Values.Where(room => !room.IsClosed && !room.Settings.IsPrivate)
			.Select(BanchoView.Match).ToList();
		outbox.Send(notification.Watcher, writer =>
		{
			foreach (var match in matches) writer.NewMatch(match, false);
		});
		return ValueTask.CompletedTask;
	}
}

/// <summary>Starts the round on the screens of the room.</summary>
internal sealed class RoundStartedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundStarted>
{
	public ValueTask Handle(RoomRoundStarted notification, CancellationToken cancellationToken)
	{
		var match = BanchoView.Match(notification.Room);
		audience.ToRoom(notification.Room, writer => writer.MatchStart(match));
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundAllLoadedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundAllLoaded>
{
	public ValueTask Handle(RoomRoundAllLoaded notification, CancellationToken cancellationToken)
	{
		audience.ToRoom(notification.Room, writer => writer.MatchAllPlayersLoaded());
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundPlayerSkippedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundPlayerSkipped>
{
	public ValueTask Handle(RoomRoundPlayerSkipped notification, CancellationToken cancellationToken)
	{
		if (notification.Room.Slots[notification.Slot].Player is { } player)
			audience.ToRoom(notification.Room, writer => writer.MatchPlayerSkipped(player.Record.Id));

		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundAllSkippedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundAllSkipped>
{
	public ValueTask Handle(RoomRoundAllSkipped notification, CancellationToken cancellationToken)
	{
		audience.ToRoom(notification.Room, writer => writer.MatchSkip());
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundPlayerFailedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundPlayerFailed>
{
	public ValueTask Handle(RoomRoundPlayerFailed notification, CancellationToken cancellationToken)
	{
		audience.ToRoom(notification.Room, writer => writer.MatchPlayerFailed(notification.Slot));
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundCompletedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundCompleted>
{
	public ValueTask Handle(RoomRoundCompleted notification, CancellationToken cancellationToken)
	{
		audience.ToRoom(notification.Room, writer => writer.MatchComplete());
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundAbortedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundAborted>
{
	public ValueTask Handle(RoomRoundAborted notification, CancellationToken cancellationToken)
	{
		audience.ToRoom(notification.Room, writer => writer.MatchAbort());
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}
