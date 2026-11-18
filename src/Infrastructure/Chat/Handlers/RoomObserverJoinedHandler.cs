using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class RoomObserverJoinedHandler(RoomChannels channels) : INotificationHandler<RoomObserverJoined>
{
	public ValueTask Handle(RoomObserverJoined @event, CancellationToken ct)
	{
		if (!@event.Room.IsClosed && channels.Find(@event.Room) is { } channel)
			channel.ScopeTo(@event.Observer).Join();

		return ValueTask.CompletedTask;
	}
}