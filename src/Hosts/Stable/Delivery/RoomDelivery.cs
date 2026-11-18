using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;
using Basil.Host.Stable.Support;
using Basil.Protocol.Stable.Wire.Packets;
using Mediator;

namespace Basil.Host.Stable.Delivery;

internal sealed class RoomAudience(Lobby lobby, Outbox outbox)
{
	public static IEnumerable<Connection> Of(Room room)
	{
		return room.Slots.Select(Connection? (slot) => slot.Player).OfType<Connection>()
			.Concat(room.Members.Observers);
	}

	public void ToRoom(Room room, Action<PacketWriter> write)
	{
		outbox.Send(Of(room), write);
	}

	public void Refresh(Room room)
	{
		if (room.IsClosed) return;
		var match = StableView.Match(room);
		outbox.Send(room.Slots.Select(Connection? (slot) => slot.Player).OfType<Connection>(),
			writer => writer.UpdateMatch(match));
		outbox.Send(room.Members.Observers, writer => writer.UpdateMatch(match, false));
		if (!room.Settings.IsPrivate) outbox.Send(lobby.Watchers, writer => writer.UpdateMatch(match, false));
	}
}

internal abstract class RoomRefreshDelivery<TEvent>(RoomAudience audience) : INotificationHandler<TEvent>
	where TEvent : RoomEvent
{
	public ValueTask Handle(TEvent notification, CancellationToken ct)
	{
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoomSettingsDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomSettingsChanged>(audience);

internal sealed class RoomLockDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomLockChanged>(audience);

internal sealed class RoomSlotTeamDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomSlotTeamChanged>(audience);

internal sealed class RoomSlotModsDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomSlotModsChanged>(audience);

internal sealed class RoomSlotStatusDelivery(RoomAudience audience)
	: RoomRefreshDelivery<RoomSlotStatusChanged>(audience);

internal sealed class RoomSlotsArrangedDelivery(RoomAudience audience)
	: RoomRefreshDelivery<RoomSlotsArranged>(audience);

internal sealed class RoomPlayerMovedDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomPlayerMoved>(audience);

internal sealed class RoomPlayerLeftDelivery(RoomAudience audience) : RoomRefreshDelivery<RoomPlayerLeft>(audience);

internal sealed class RoomHostDelivery(RoomAudience audience, Outbox outbox) : INotificationHandler<RoomHostChanged>
{
	public ValueTask Handle(RoomHostChanged notification, CancellationToken ct)
	{
		if (notification.Host is { } host) outbox.Send(host, writer => writer.MatchTransferHost());
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoomPlayerJoinedDelivery(RoomAudience audience, Outbox outbox)
	: INotificationHandler<RoomPlayerJoined>
{
	public ValueTask Handle(RoomPlayerJoined notification, CancellationToken ct)
	{
		var match = StableView.Match(notification.Room);
		outbox.Send(notification.Player, writer => writer.MatchJoinSuccess(match));
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoomPlayerKickedDelivery(RoomAudience audience, Outbox outbox)
	: INotificationHandler<RoomPlayerKicked>
{
	public ValueTask Handle(RoomPlayerKicked notification, CancellationToken ct)
	{
		outbox.Send(notification.Player, writer => writer.DisposeMatch(notification.Room.Id));
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoomPlayerBannedDelivery(RoomAudience audience, Outbox outbox)
	: INotificationHandler<RoomPlayerBanned>
{
	public ValueTask Handle(RoomPlayerBanned notification, CancellationToken ct)
	{
		if (notification.Evicted is { } evicted)
			outbox.Send(evicted, writer => writer.DisposeMatch(notification.Room.Id));
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoomSlotLockDelivery(RoomAudience audience, Outbox outbox)
	: INotificationHandler<RoomSlotLockChanged>
{
	public ValueTask Handle(RoomSlotLockChanged notification, CancellationToken ct)
	{
		if (notification.Evicted is { } evicted)
			outbox.Send(evicted, writer => writer.DisposeMatch(notification.Room.Id));
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoomObserverDelivery(Outbox outbox) : INotificationHandler<RoomObserverJoined>
{
	public ValueTask Handle(RoomObserverJoined notification, CancellationToken ct)
	{
		var match = StableView.Match(notification.Room);
		outbox.Send(notification.Observer, writer => writer.UpdateMatch(match, false));
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoomInviteDelivery(UserRegistry registry, Outbox outbox) : INotificationHandler<RoomPlayerInvited>
{
	public ValueTask Handle(RoomPlayerInvited notification, CancellationToken ct)
	{
		if (registry.OpenStableClient(notification.Player) is { } target)
			outbox.Send(target, writer => writer.MatchInvite(notification.Actor.Id, notification.Actor.Value.Name,
				$"Come join my game: {notification.Room.UrlEmbed}", notification.Player.Value.Name));

		return ValueTask.CompletedTask;
	}
}

internal sealed class LobbyRoomOpenedDelivery(Lobby lobby, Outbox outbox) : INotificationHandler<LobbyRoomOpened>
{
	public ValueTask Handle(LobbyRoomOpened notification, CancellationToken ct)
	{
		var match = StableView.Match(notification.Room);
		if (!notification.Room.Settings.IsPrivate) outbox.Send(lobby.Watchers, writer => writer.NewMatch(match, false));
		if (notification.Host is { } host) outbox.Send(host, writer => writer.MatchJoinSuccess(match));
		return ValueTask.CompletedTask;
	}
}

internal sealed class LobbyRoomClosedDelivery(Lobby lobby, Outbox outbox) : INotificationHandler<LobbyRoomClosed>
{
	public ValueTask Handle(LobbyRoomClosed notification, CancellationToken ct)
	{
		var id = notification.Room.Id;
		outbox.Send(lobby.Watchers.Concat<Connection>(notification.Evicted), writer => writer.DisposeMatch(id));
		return ValueTask.CompletedTask;
	}
}

internal sealed class LobbyWatcherDelivery(Lobby lobby, Outbox outbox) : INotificationHandler<LobbyWatcherJoined>
{
	public ValueTask Handle(LobbyWatcherJoined notification, CancellationToken ct)
	{
		var matches = lobby.Rooms.Values.Where(room => room is { IsClosed: false, Settings.IsPrivate: false })
			.Select(StableView.Match).ToList();
		outbox.Send(notification.Watcher, writer =>
		{
			foreach (var match in matches) writer.NewMatch(match, false);
		});
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundStartedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundStarted>
{
	public ValueTask Handle(RoomRoundStarted notification, CancellationToken ct)
	{
		var match = StableView.Match(notification.Room);
		audience.ToRoom(notification.Room, writer => writer.MatchStart(match));
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundAllLoadedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundAllLoaded>
{
	public ValueTask Handle(RoomRoundAllLoaded notification, CancellationToken ct)
	{
		audience.ToRoom(notification.Room, writer => writer.MatchAllPlayersLoaded());
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundPlayerSkippedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundPlayerSkipped>
{
	public ValueTask Handle(RoomRoundPlayerSkipped notification, CancellationToken ct)
	{
		if (notification.Room.Slots[notification.Slot].Player is { } player)
			audience.ToRoom(notification.Room, writer => writer.MatchPlayerSkipped(player.Record.Id));

		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundAllSkippedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundAllSkipped>
{
	public ValueTask Handle(RoomRoundAllSkipped notification, CancellationToken ct)
	{
		audience.ToRoom(notification.Room, writer => writer.MatchSkip());
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundPlayerFailedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundPlayerFailed>
{
	public ValueTask Handle(RoomRoundPlayerFailed notification, CancellationToken ct)
	{
		audience.ToRoom(notification.Room, writer => writer.MatchPlayerFailed(notification.Slot));
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundCompletedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundCompleted>
{
	public ValueTask Handle(RoomRoundCompleted notification, CancellationToken ct)
	{
		audience.ToRoom(notification.Room, writer => writer.MatchComplete());
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundAbortedDelivery(RoomAudience audience) : INotificationHandler<RoomRoundAborted>
{
	public ValueTask Handle(RoomRoundAborted notification, CancellationToken ct)
	{
		audience.ToRoom(notification.Room, writer => writer.MatchAbort());
		audience.Refresh(notification.Room);
		return ValueTask.CompletedTask;
	}
}