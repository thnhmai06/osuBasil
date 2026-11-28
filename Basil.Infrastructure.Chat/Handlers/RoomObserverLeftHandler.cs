using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class RoomObserverLeftHandler(RoomChannels channels) : INotificationHandler<RoomObserverLeft>
{
	public ValueTask Handle(RoomObserverLeft @event, CancellationToken ct)
	{
		if (channels.Find(@event.Room) is { } channel) channel.Release(@event.Observer);

		return ValueTask.CompletedTask;
	}
}