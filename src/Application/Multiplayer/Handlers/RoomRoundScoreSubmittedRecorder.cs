using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Mediator;

namespace Basil.Application.Multiplayer.Handlers;

/// <summary>Stores which round a submitted score belongs to.</summary>
internal sealed class RoomRoundScoreSubmittedRecorder(IRoundScoreRepository roundScores)
	: INotificationHandler<RoomRoundScoreSubmitted>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundScoreSubmitted @event, CancellationToken cancellationToken)
	{
		await roundScores.RecordAsync(@event.Score, cancellationToken);
	}
}