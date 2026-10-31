using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Application.Chat.Handlers;

/// <summary>Opens the chat channel of a new room and adds its first host to it.</summary>
internal sealed class LobbyRoomOpenedHandler(RoomChannels channels) : INotificationHandler<LobbyRoomOpened>
{
	/// <inheritdoc />
	public ValueTask Handle(LobbyRoomOpened @event, CancellationToken cancellationToken)
	{
		if (@event.Room.IsClosed) return ValueTask.CompletedTask;

		var channel = channels.Of(@event.Room);
		if (@event.Host is { } host) channel.ScopeTo(host).Join();

		return ValueTask.CompletedTask;
	}
}