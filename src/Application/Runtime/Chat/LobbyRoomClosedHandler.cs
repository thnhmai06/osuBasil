using Basil.Application.Runtime.Multiplayer.Events;
using Mediator;

namespace Basil.Application.Runtime.Chat;

/// <summary>Closes and forgets the chat channel of a closed room.</summary>
internal sealed class LobbyRoomClosedHandler(RoomChannels channels) : INotificationHandler<LobbyRoomClosed>
{
	/// <inheritdoc />
	public ValueTask Handle(LobbyRoomClosed @event, CancellationToken cancellationToken)
	{
		channels.Remove(@event.Room)?.Close();
		return ValueTask.CompletedTask;
	}
}