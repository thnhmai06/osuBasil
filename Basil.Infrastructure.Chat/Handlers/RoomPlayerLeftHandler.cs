using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class RoomPlayerLeftHandler(RoomChannels channels) : INotificationHandler<RoomPlayerLeft>
{
	public ValueTask Handle(RoomPlayerLeft @event, CancellationToken ct)
	{
		if (channels.Find(@event.Room) is { } channel) channel.Release(@event.Player);

		return ValueTask.CompletedTask;
	}
}