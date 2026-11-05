using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

/// <summary>Parts a former referee from the room's chat channel unless they are still involved in the room.</summary>
internal sealed class RoomRefereeRemovedHandler(RoomChannels channels, TimeProvider clock)
	: INotificationHandler<RoomRefereeRemoved>
{
	/// <inheritdoc />
	public ValueTask Handle(RoomRefereeRemoved @event, CancellationToken cancellationToken)
	{
		if (channels.Find(@event.Room) is not { } channel) return ValueTask.CompletedTask;

		var now = clock.GetUtcNow();
		foreach (var member in channel.Members.Where(member => member.Record.Equals(@event.Referee) &&
		                                                       !@event.Room.Involves(member, now)))
			channel.ScopeTo(member).Part();

		return ValueTask.CompletedTask;
	}
}