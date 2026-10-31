using Basil.Application.Multiplayer.Objects.Events;
using Mediator;
using Basil.Application.Chat.Registry;

namespace Basil.Application.Chat.Handlers;

/// <summary>Parts a room observer from the room's chat channel unless they manage the room.</summary>
internal sealed class RoomObserverLeftHandler(RoomChannels channels) : INotificationHandler<RoomObserverLeft>
{
	/// <inheritdoc />
	public ValueTask Handle(RoomObserverLeft @event, CancellationToken cancellationToken)
	{
		if (channels.Find(@event.Room) is { } channel && !@event.Room.Authority.IsManagedBy(@event.Observer.Record))
			channel.ScopeTo(@event.Observer).Part();

		return ValueTask.CompletedTask;
	}
}