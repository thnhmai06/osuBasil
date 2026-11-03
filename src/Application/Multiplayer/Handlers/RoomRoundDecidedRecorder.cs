using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Mediator;

namespace Basil.Application.Multiplayer.Handlers;

/// <summary>Stores a round with its decision.</summary>
internal sealed class RoomRoundDecidedRecorder(IRoundRepository rounds) : INotificationHandler<RoomRoundDecided>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundDecided @event, CancellationToken cancellationToken)
	{
		await rounds.CreateOrUpdateAsync(@event.Round, cancellationToken);
	}
}