using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Mediator;

namespace Basil.Application.Multiplayer.Handlers;

/// <summary>Stores a completed round with its end.</summary>
internal sealed class RoomRoundCompletedRecorder(IRoundRepository rounds) : INotificationHandler<RoomRoundCompleted>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundCompleted @event, CancellationToken cancellationToken)
	{
		await rounds.CreateOrUpdateAsync(@event.Round, cancellationToken);
	}
}