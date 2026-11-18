using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class LobbyRoomClosedRecorder(
	IRoundRepository rounds,
	IMatchRepository matches,
	IMatchEventRepository matchEvents) : INotificationHandler<LobbyRoomClosed>
{
	public async ValueTask Handle(LobbyRoomClosed @event, CancellationToken ct)
	{
		if (@event.AbortedRound is { } abortedRound)
			await rounds.CreateOrUpdateAsync(abortedRound);
		await matchEvents.RecordAsync(
			new MatchEvent(@event.Room.Match, MatchEventType.Closed, @event.Timestamp, @event.Actor, null,
				@event.Actor is null ? "Empty" : null));
		// The match reads as ended only once its report is complete.
		await matches.UpdateAsync(@event.Room.Match);
	}
}