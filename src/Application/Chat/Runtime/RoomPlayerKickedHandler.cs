using Basil.Application.Multiplayer.Runtime.Events;
using Mediator;

namespace Basil.Application.Chat.Runtime;

/// <summary>Parts a removed player from a room's chat channel.</summary>
internal sealed class RoomPlayerKickedHandler(RoomChannels channels) : INotificationHandler<RoomPlayerKicked>
{
	/// <inheritdoc />
	public ValueTask Handle(RoomPlayerKicked @event, CancellationToken cancellationToken)
	{
		if (channels.Find(@event.Room) is { } channel && !@event.Room.Authority.IsManagedBy(@event.Player.Record))
			channel.ScopeTo(@event.Player).Part();

		return ValueTask.CompletedTask;
	}
}