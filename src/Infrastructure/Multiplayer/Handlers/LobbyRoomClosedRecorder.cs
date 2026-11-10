using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Stores the round a closing room aborted, ends its match and records who closed it.</summary>
internal sealed class LobbyRoomClosedRecorder(
	IRoundRepository rounds,
	IMatchRepository matches,
	IMatchEventRepository matchEvents) : INotificationHandler<LobbyRoomClosed>
{
	/// <inheritdoc />
	public async ValueTask Handle(LobbyRoomClosed @event, CancellationToken cancellationToken)
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