using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Stores the rounds a recovery aborted, ends the recovered match and records the recovery.</summary>
internal sealed class LobbyMatchRecoveredRecorder(
	IRoundRepository rounds,
	IMatchRepository matches,
	IMatchEventRepository matchEvents) : INotificationHandler<LobbyMatchRecovered>
{
	/// <inheritdoc />
	public async ValueTask Handle(LobbyMatchRecovered @event, CancellationToken cancellationToken)
	{
		foreach (var round in @event.AbortedRounds)
			await rounds.CreateOrUpdateAsync(round, cancellationToken);
		await matchEvents.RecordAsync(
			new MatchEvent(@event.Match, MatchEventType.Closed, @event.Timestamp, null, null,
				MatchEvent.ServerStoppedDetail), cancellationToken);
		// The match reads as ended only once its report is complete.
		await matches.UpdateAsync(@event.Match, cancellationToken);
	}
}