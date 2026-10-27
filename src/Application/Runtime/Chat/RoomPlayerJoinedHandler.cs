using Basil.Application.Runtime.Multiplayer.Events;
using Mediator;

namespace Basil.Application.Runtime.Chat;

/// <summary>Keeps the chat channel of an open room in step when a player joins or replaces a connection.</summary>
internal sealed class RoomPlayerJoinedHandler(RoomChannels channels) : INotificationHandler<RoomPlayerJoined>
{
	/// <inheritdoc />
	public ValueTask Handle(RoomPlayerJoined @event, CancellationToken cancellationToken)
	{
		if (@event.Room.IsClosed || channels.Find(@event.Room) is not { } channel)
			return ValueTask.CompletedTask;

		if (@event.Replaced is { } replaced && !@event.Room.Authority.IsManagedBy(replaced.Record))
			channel.ScopeTo(replaced).Part();
		channel.ScopeTo(@event.Player).Join();
		return ValueTask.CompletedTask;
	}
}