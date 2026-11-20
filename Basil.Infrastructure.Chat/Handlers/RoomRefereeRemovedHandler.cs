using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class RoomRefereeRemovedHandler(RoomChannels channels, TimeProvider clock)
	: INotificationHandler<RoomRefereeRemoved>
{
	public ValueTask Handle(RoomRefereeRemoved @event, CancellationToken ct)
	{
		if (channels.Find(@event.Room) is not { } channel) return ValueTask.CompletedTask;

		var now = clock.GetUtcNow();
		foreach (var member in channel.Members.Where(member => member.Record.Equals(@event.Referee) &&
		                                                       !@event.Room.Involves(member,
			                                                       member.User.EffectivePrivileges(now))))
			channel.ScopeTo(member).Part();

		return ValueTask.CompletedTask;
	}
}