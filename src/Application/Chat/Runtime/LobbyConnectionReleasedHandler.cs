using Basil.Application.Multiplayer.Runtime.Events;
using Basil.Application.Multiplayer.Runtime;
using Mediator;

namespace Basil.Application.Chat.Runtime;

/// <summary>Parts a released connection from every open room's chat channel.</summary>
internal sealed class LobbyConnectionReleasedHandler(Lobby lobby, RoomChannels channels)
	: INotificationHandler<LobbyConnectionReleased>
{
	/// <inheritdoc />
	public ValueTask Handle(LobbyConnectionReleased @event, CancellationToken cancellationToken)
	{
		foreach (var room in lobby.Rooms.Values)
			if (channels.Find(room) is { } channel)
				channel.ScopeTo(@event.Connection).Part();

		return ValueTask.CompletedTask;
	}
}