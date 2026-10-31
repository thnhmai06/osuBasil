using Basil.Application.Multiplayer.Objects.Events;
using Mediator;
using Basil.Application.Chat.Registry;

namespace Basil.Application.Chat.Handlers;

/// <summary>Adds a room observer to the room's chat channel.</summary>
internal sealed class RoomObserverJoinedHandler(RoomChannels channels) : INotificationHandler<RoomObserverJoined>
{
	/// <inheritdoc />
	public ValueTask Handle(RoomObserverJoined @event, CancellationToken cancellationToken)
	{
		if (!@event.Room.IsClosed && channels.Find(@event.Room) is { } channel)
			channel.ScopeTo(@event.Observer).Join();

		return ValueTask.CompletedTask;
	}
}