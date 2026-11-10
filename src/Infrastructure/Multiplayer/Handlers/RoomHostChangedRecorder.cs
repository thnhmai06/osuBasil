using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Records a host being granted to a player.</summary>
internal sealed class RoomHostChangedRecorder(IMatchEventRepository matchEvents) : INotificationHandler<RoomHostChanged>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomHostChanged @event, CancellationToken cancellationToken)
	{
		if (@event.Host is { } host)
			await matchEvents.RecordAsync(
				new MatchEvent(@event.Room.Match, MatchEventType.HostGranted, @event.Timestamp, @event.Actor,
					host.Record));
	}
}