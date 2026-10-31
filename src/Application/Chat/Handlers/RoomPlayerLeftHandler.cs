using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Application.Chat.Handlers;

/// <summary>Parts a player who does not manage the room from its chat channel when they leave their seat.</summary>
internal sealed class RoomPlayerLeftHandler(RoomChannels channels) : INotificationHandler<RoomPlayerLeft>
{
	/// <inheritdoc />
	public ValueTask Handle(RoomPlayerLeft @event, CancellationToken cancellationToken)
	{
		if (channels.Find(@event.Room) is { } channel && !@event.Room.Authority.IsManagedBy(@event.Player.Record))
			channel.ScopeTo(@event.Player).Part();

		return ValueTask.CompletedTask;
	}
}