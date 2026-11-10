using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

/// <summary>Adds a room observer to the room's chat channel.</summary>
internal sealed class RoomObserverJoinedHandler(RoomChannels channels) : INotificationHandler<RoomObserverJoined>
{
	/// <inheritdoc />
	public ValueTask Handle(RoomObserverJoined @event, CancellationToken ct)
	{
		if (!@event.Room.IsClosed && channels.Find(@event.Room) is { } channel)
			channel.ScopeTo(@event.Observer).Join();

		return ValueTask.CompletedTask;
	}
}