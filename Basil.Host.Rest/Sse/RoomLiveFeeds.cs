using Basil.Application.Multiplayer.Objects.Events;
using Basil.Host.Rest.Routes;
using Basil.Host.Shared.Views;
using Mediator;

namespace Basil.Host.Rest.Sse;

/// <summary>Sends the room as it is to the streams of a room when an event changed it.</summary>
internal abstract class RoomLiveFeed<TEvent>(SseHub hub) : INotificationHandler<TEvent>
	where TEvent : RoomEvent
{
	public ValueTask Handle(TEvent notification, CancellationToken ct)
	{
		var key = RoomRoutes.KeyOf(notification.Room.Id);
		if (hub.IsWatched(key)) hub.Publish(key, new { type = "room", room = RoomView.Of(notification.Room) });
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoomLockLiveFeed(SseHub hub) : RoomLiveFeed<RoomLockChanged>(hub);

internal sealed class RoomSlotLockLiveFeed(SseHub hub) : RoomLiveFeed<RoomSlotLockChanged>(hub);

internal sealed class RoomSlotTeamLiveFeed(SseHub hub) : RoomLiveFeed<RoomSlotTeamChanged>(hub);

internal sealed class RoomSlotModsLiveFeed(SseHub hub) : RoomLiveFeed<RoomSlotModsChanged>(hub);

internal sealed class RoomSlotStatusLiveFeed(SseHub hub) : RoomLiveFeed<RoomSlotStatusChanged>(hub);

internal sealed class RoomSlotsArrangedLiveFeed(SseHub hub) : RoomLiveFeed<RoomSlotsArranged>(hub);

internal sealed class RoomPlayerJoinedLiveFeed(SseHub hub) : RoomLiveFeed<RoomPlayerJoined>(hub);

internal sealed class RoomPlayerLeftLiveFeed(SseHub hub) : RoomLiveFeed<RoomPlayerLeft>(hub);

internal sealed class RoomPlayerKickedLiveFeed(SseHub hub) : RoomLiveFeed<RoomPlayerKicked>(hub);

internal sealed class RoomPlayerMovedLiveFeed(SseHub hub) : RoomLiveFeed<RoomPlayerMoved>(hub);

internal sealed class RoomSettingsLiveFeed(SseHub hub) : RoomLiveFeed<RoomSettingsChanged>(hub);

internal sealed class RoomHostLiveFeed(SseHub hub) : RoomLiveFeed<RoomHostChanged>(hub);

internal sealed class RoomRefereeAddedLiveFeed(SseHub hub) : RoomLiveFeed<RoomRefereeAdded>(hub);

internal sealed class RoomRefereeRemovedLiveFeed(SseHub hub) : RoomLiveFeed<RoomRefereeRemoved>(hub);

internal sealed class RoomPlayerBannedLiveFeed(SseHub hub) : RoomLiveFeed<RoomPlayerBanned>(hub);

internal sealed class RoomPlayerUnbannedLiveFeed(SseHub hub) : RoomLiveFeed<RoomPlayerUnbanned>(hub);

internal sealed class RoomRoundStartedLiveFeed(SseHub hub) : RoomLiveFeed<RoomRoundStarted>(hub);

internal sealed class RoomRoundAbortedLiveFeed(SseHub hub) : RoomLiveFeed<RoomRoundAborted>(hub);

internal sealed class RoomRoundEndedLiveFeed(SseHub hub) : RoomLiveFeed<RoomRoundEnded>(hub);

internal sealed class RoomRoundDecidedLiveFeed(SseHub hub) : RoomLiveFeed<RoomRoundDecided>(hub);

/// <summary>Ends the streams of a room when the room closes.</summary>
internal sealed class RoomClosedLiveFeed(SseHub hub) : INotificationHandler<LobbyRoomClosed>
{
	public ValueTask Handle(LobbyRoomClosed notification, CancellationToken ct)
	{
		hub.Finish(RoomRoutes.KeyOf(notification.Room.Id), new { type = "closed" });
		return ValueTask.CompletedTask;
	}
}
