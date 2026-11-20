using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class RoomSlotLockChangedHandler(RoomChannels channels) : INotificationHandler<RoomSlotLockChanged>
{
	public ValueTask Handle(RoomSlotLockChanged @event, CancellationToken ct)
	{
		if (@event.Evicted is { } evicted && channels.Find(@event.Room) is { } channel &&
		    !@event.Room.IsManagedBy(evicted.Record))
			channel.ScopeTo(evicted).Part();

		return ValueTask.CompletedTask;
	}
}