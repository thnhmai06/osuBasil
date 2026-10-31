using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Application.Chat.Handlers;

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