using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Records a player being banned, and the round the departure ended.</summary>
internal sealed class RoomPlayerBannedRecorder(
	IRoundRepository rounds,
	IMatchEventRepository matchEvents) : INotificationHandler<RoomPlayerBanned>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomPlayerBanned @event, CancellationToken cancellationToken)
	{
		if (@event.RoundProgress is { Completed: true } progress)
			await rounds.CreateOrUpdateAsync(progress.Round);
		await matchEvents.RecordAsync(
			new MatchEvent(@event.Room.Match, MatchEventType.Kicked, @event.Timestamp, @event.Actor, @event.Player,
				"Banned"));
	}
}