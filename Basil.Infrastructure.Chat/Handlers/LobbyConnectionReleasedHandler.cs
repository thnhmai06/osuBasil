using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Registry;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class LobbyConnectionReleasedHandler(Lobby lobby, RoomChannels channels)
	: INotificationHandler<LobbyConnectionReleased>
{
	public ValueTask Handle(LobbyConnectionReleased @event, CancellationToken ct)
	{
		foreach (var room in lobby.Rooms.Values)
			if (channels.Find(room) is { } channel)
				channel.ScopeTo(@event.Session).Part();

		return ValueTask.CompletedTask;
	}
}