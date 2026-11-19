using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer.Report.Matches;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class RoomPlayerJoinedRecorder(
	IRoundRepository rounds,
	IMatchEventRepository matchEvents) : INotificationHandler<RoomPlayerJoined>
{
	public async ValueTask Handle(RoomPlayerJoined @event, CancellationToken ct)
	{
		if (@event.RoundProgress is { Completed: true } progress)
			await rounds.CreateOrUpdateAsync(progress.Round);
		await matchEvents.RecordAsync(
			new MatchEvent(@event.Room.Match, MatchEventType.PlayerJoined, @event.Timestamp, null,
				@event.Player.Record));
	}
}