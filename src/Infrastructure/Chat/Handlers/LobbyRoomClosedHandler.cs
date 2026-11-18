using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class LobbyRoomClosedHandler(RoomChannels channels) : INotificationHandler<LobbyRoomClosed>
{
	public ValueTask Handle(LobbyRoomClosed @event, CancellationToken ct)
	{
		channels.Remove(@event.Room)?.Close();
		return ValueTask.CompletedTask;
	}
}