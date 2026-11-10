using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Records a player being kicked, and the round the departure ended.</summary>
internal sealed class RoomPlayerKickedRecorder(
	IRoundRepository rounds,
	IMatchEventRepository matchEvents) : INotificationHandler<RoomPlayerKicked>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomPlayerKicked @event, CancellationToken ct)
	{
		if (@event.RoundProgress is { Completed: true } progress)
			await rounds.CreateOrUpdateAsync(progress.Round);
		await matchEvents.RecordAsync(
			new MatchEvent(@event.Room.Match, MatchEventType.Kicked, @event.Timestamp, @event.Actor,
				@event.Player.Record, "Kicked"));
	}
}