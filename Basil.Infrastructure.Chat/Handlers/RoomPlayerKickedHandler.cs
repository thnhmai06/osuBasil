using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class RoomPlayerKickedHandler(RoomChannels channels) : INotificationHandler<RoomPlayerKicked>
{
	public ValueTask Handle(RoomPlayerKicked @event, CancellationToken ct)
	{
		if (channels.Find(@event.Room) is { } channel) channel.Release(@event.Player);

		return ValueTask.CompletedTask;
	}
}