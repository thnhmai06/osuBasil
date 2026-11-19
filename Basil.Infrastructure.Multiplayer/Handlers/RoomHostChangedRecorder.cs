using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer.Report.Matches;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class RoomHostChangedRecorder(IMatchEventRepository matchEvents) : INotificationHandler<RoomHostChanged>
{
	public async ValueTask Handle(RoomHostChanged @event, CancellationToken ct)
	{
		if (@event.Host is { } host)
			await matchEvents.RecordAsync(
				new MatchEvent(@event.Room.Match, MatchEventType.HostGranted, @event.Timestamp, @event.Actor,
					host.Record));
	}
}