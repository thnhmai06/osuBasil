using Basil.Application.Multiplayer.Runtime.Events;
using Mediator;

namespace Basil.Application.Chat.Runtime;

/// <summary>Parts a banned player from a room's chat channel when their seat is vacated.</summary>
internal sealed class RoomPlayerBannedHandler(RoomChannels channels) : INotificationHandler<RoomPlayerBanned>
{
	/// <inheritdoc />
	public ValueTask Handle(RoomPlayerBanned @event, CancellationToken cancellationToken)
	{
		if (@event.Evicted is { } evicted && channels.Find(@event.Room) is { } channel &&
		    !@event.Room.Authority.IsManagedBy(evicted.Record))
			channel.ScopeTo(evicted).Part();

		return ValueTask.CompletedTask;
	}
}