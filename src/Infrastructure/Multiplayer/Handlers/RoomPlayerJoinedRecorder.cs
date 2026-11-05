using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Records a player joining, and the round the arrival ended.</summary>
internal sealed class RoomPlayerJoinedRecorder(
	IRoundRepository rounds,
	IMatchEventRepository matchEvents) : INotificationHandler<RoomPlayerJoined>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomPlayerJoined @event, CancellationToken cancellationToken)
	{
		if (@event.RoundProgress is { Completed: true } progress)
			await rounds.CreateOrUpdateAsync(progress.Round, cancellationToken);
		await matchEvents.RecordAsync(
			new MatchEvent(@event.Room.Match, MatchEventType.PlayerJoined, @event.Timestamp, null,
				@event.Player.Record), cancellationToken);
	}
}