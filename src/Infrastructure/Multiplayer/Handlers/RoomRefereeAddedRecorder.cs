using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Records a referee being added.</summary>
internal sealed class RoomRefereeAddedRecorder(IMatchEventRepository matchEvents)
	: INotificationHandler<RoomRefereeAdded>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomRefereeAdded @event, CancellationToken ct)
	{
		await matchEvents.RecordAsync(
			new MatchEvent(@event.Room.Match, MatchEventType.RefAdded, @event.Timestamp, @event.Actor, @event.Referee));
	}
}