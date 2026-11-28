using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class RoomRoundScoreSubmittedRecorder(IRoundScoreRepository roundScores)
	: INotificationHandler<RoomRoundScoreSubmitted>
{
	public async ValueTask Handle(RoomRoundScoreSubmitted @event, CancellationToken ct)
	{
		await roundScores.RecordAsync(@event.Score);
	}
}