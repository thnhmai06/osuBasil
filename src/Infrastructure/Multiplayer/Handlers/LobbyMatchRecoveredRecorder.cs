using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer.Report.Matches;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class LobbyMatchRecoveredRecorder(
	IRoundRepository rounds,
	IMatchRepository matches,
	IMatchEventRepository matchEvents) : INotificationHandler<LobbyMatchRecovered>
{
	public async ValueTask Handle(LobbyMatchRecovered @event, CancellationToken ct)
	{
		foreach (var round in @event.AbortedRounds)
			await rounds.CreateOrUpdateAsync(round);
		await matchEvents.RecordAsync(
			new MatchEvent(@event.Match, MatchEventType.Closed, @event.Timestamp, null, null,
				MatchEvent.ServerStoppedDetail));
		// The match reads as ended only once its report is complete.
		await matches.UpdateAsync(@event.Match);
	}
}