using Basil.Application.Multiplayer.Runtime.Events;
using Mediator;

namespace Basil.Application.Chat.Runtime;

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