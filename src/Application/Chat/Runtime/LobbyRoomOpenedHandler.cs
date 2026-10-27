using Basil.Application.Multiplayer.Runtime.Events;
using Mediator;

namespace Basil.Application.Chat.Runtime;

/// <summary>Adds a room's first host to its chat channel when that channel is open.</summary>
internal sealed class LobbyRoomOpenedHandler(RoomChannels channels) : INotificationHandler<LobbyRoomOpened>
{
	/// <inheritdoc />
	public ValueTask Handle(LobbyRoomOpened @event, CancellationToken cancellationToken)
	{
		if (!@event.Room.IsClosed && @event.Host is { } host && channels.Find(@event.Room) is { } channel)
			channel.ScopeTo(host).Join();

		return ValueTask.CompletedTask;
	}
}