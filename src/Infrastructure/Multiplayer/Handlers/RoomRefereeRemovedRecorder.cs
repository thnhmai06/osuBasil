using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class RoomRefereeRemovedRecorder(IMatchEventRepository matchEvents)
	: INotificationHandler<RoomRefereeRemoved>
{
	public async ValueTask Handle(RoomRefereeRemoved @event, CancellationToken ct)
	{
		await matchEvents.RecordAsync(
			new MatchEvent(@event.Room.Match, MatchEventType.RefRemoved, @event.Timestamp, @event.Actor,
				@event.Referee));
	}
}