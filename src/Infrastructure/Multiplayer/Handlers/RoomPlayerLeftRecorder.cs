using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class RoomPlayerLeftRecorder(
	IRoundRepository rounds,
	IMatchEventRepository matchEvents) : INotificationHandler<RoomPlayerLeft>
{
	public async ValueTask Handle(RoomPlayerLeft @event, CancellationToken ct)
	{
		if (@event.RoundProgress is { Completed: true } progress)
			await rounds.CreateOrUpdateAsync(progress.Round);
		await matchEvents.RecordAsync(
			new MatchEvent(@event.Room.Match, MatchEventType.PlayerLeft, @event.Timestamp, null, @event.Player.Record));
	}
}