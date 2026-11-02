using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Application.Multiplayer.Handlers;

/// <summary>Stores an aborted round.</summary>
internal sealed class RoomRoundAbortedRecorder(IRoundRepository rounds) : INotificationHandler<RoomRoundAborted>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundAborted @event, CancellationToken cancellationToken)
	{
		await rounds.CreateOrUpdateAsync(@event.Round, cancellationToken);
	}
}
