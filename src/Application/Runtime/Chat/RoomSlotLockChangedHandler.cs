using Basil.Application.Runtime.Multiplayer.Events;
using Mediator;

namespace Basil.Application.Runtime.Chat;

/// <summary>Parts a player from a room's chat channel when locking their slot removes them.</summary>
internal sealed class RoomSlotLockChangedHandler(RoomChannels channels) : INotificationHandler<RoomSlotLockChanged>
{
	/// <inheritdoc />
	public ValueTask Handle(RoomSlotLockChanged @event, CancellationToken cancellationToken)
	{
		if (@event.Evicted is { } evicted && channels.Find(@event.Room) is { } channel &&
		    !@event.Room.Authority.IsManagedBy(evicted.Record))
			channel.ScopeTo(evicted).Part();

		return ValueTask.CompletedTask;
	}
}