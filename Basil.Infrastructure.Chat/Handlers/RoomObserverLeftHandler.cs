using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class RoomObserverLeftHandler(RoomChannels channels) : INotificationHandler<RoomObserverLeft>
{
	public ValueTask Handle(RoomObserverLeft @event, CancellationToken ct)
	{
		if (channels.Find(@event.Room) is { } channel && !@event.Room.IsManagedBy(@event.Observer.Record))
			channel.ScopeTo(@event.Observer).Part();

		return ValueTask.CompletedTask;
	}
}