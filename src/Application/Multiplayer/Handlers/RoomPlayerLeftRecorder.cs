using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Application.Multiplayer.Handlers;

/// <summary>Records a player leaving, and the round the departure ended.</summary>
internal sealed class RoomPlayerLeftRecorder(
	IRoundRepository rounds,
	IMatchEventRepository matchEvents) : INotificationHandler<RoomPlayerLeft>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomPlayerLeft @event, CancellationToken cancellationToken)
	{
		if (@event.RoundProgress is { Completed: true } progress)
			await rounds.CreateOrUpdateAsync(progress.Round, cancellationToken);
		await matchEvents.RecordAsync(
			new MatchEvent(@event.Room.Match, MatchEventType.PlayerLeft, @event.Timestamp, null, @event.Player.Record),
			cancellationToken);
	}
}