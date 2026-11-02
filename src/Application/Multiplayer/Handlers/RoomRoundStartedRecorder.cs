using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Application.Multiplayer.Handlers;

/// <summary>Stores a started round.</summary>
internal sealed class RoomRoundStartedRecorder(IRoundRepository rounds) : INotificationHandler<RoomRoundStarted>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundStarted @event, CancellationToken cancellationToken)
	{
		await rounds.CreateOrUpdateAsync(@event.Round, cancellationToken);
	}
}
