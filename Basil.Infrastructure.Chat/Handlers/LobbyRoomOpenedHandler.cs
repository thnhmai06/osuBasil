using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class LobbyRoomOpenedHandler(RoomChannels channels) : INotificationHandler<LobbyRoomOpened>
{
	public ValueTask Handle(LobbyRoomOpened @event, CancellationToken ct)
	{
		if (@event.Room.IsClosed) return ValueTask.CompletedTask;

		var channel = channels.Of(@event.Room);
		if (@event.Host is { } host) channel.ScopeTo(host).Join();

		return ValueTask.CompletedTask;
	}
}