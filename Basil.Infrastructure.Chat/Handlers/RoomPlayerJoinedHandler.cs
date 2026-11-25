using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class RoomPlayerJoinedHandler(RoomChannels channels) : INotificationHandler<RoomPlayerJoined>
{
	public ValueTask Handle(RoomPlayerJoined @event, CancellationToken ct)
	{
		if (@event.Room.IsClosed || channels.Find(@event.Room) is not { } channel)
			return ValueTask.CompletedTask;

		if (@event.Replaced is { } replaced) channel.Release(replaced);
		channel.ScopeTo(@event.Player).Join();
		return ValueTask.CompletedTask;
	}
}