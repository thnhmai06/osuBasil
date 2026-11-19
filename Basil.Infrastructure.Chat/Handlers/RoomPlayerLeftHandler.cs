using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class RoomPlayerLeftHandler(RoomChannels channels) : INotificationHandler<RoomPlayerLeft>
{
	public ValueTask Handle(RoomPlayerLeft @event, CancellationToken ct)
	{
		if (channels.Find(@event.Room) is { } channel && !@event.Room.IsManagedBy(@event.Player.Record))
			channel.ScopeTo(@event.Player).Part();

		return ValueTask.CompletedTask;
	}
}