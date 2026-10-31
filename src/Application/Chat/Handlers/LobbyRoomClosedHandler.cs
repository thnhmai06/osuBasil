using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Application.Chat.Handlers;

/// <summary>Closes and forgets the chat channel of a closed room.</summary>
public sealed class LobbyRoomClosedHandler(RoomChannels channels) : INotificationHandler<LobbyRoomClosed>
{
	/// <inheritdoc />
	public ValueTask Handle(LobbyRoomClosed @event, CancellationToken cancellationToken)
	{
		channels.Remove(@event.Room)?.Close();
		return ValueTask.CompletedTask;
	}
}