using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

/// <summary>Closes and forgets the chat channel of a closed room.</summary>
internal sealed class LobbyRoomClosedHandler(RoomChannels channels) : INotificationHandler<LobbyRoomClosed>
{
	/// <inheritdoc />
	public ValueTask Handle(LobbyRoomClosed @event, CancellationToken ct)
	{
		channels.Remove(@event.Room)?.Close();
		return ValueTask.CompletedTask;
	}
}