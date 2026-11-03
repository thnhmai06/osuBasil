using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Mediator;

namespace Basil.Application.Multiplayer.Handlers;

/// <summary>Stores the round a slot lock change ended.</summary>
internal sealed class RoomSlotLockChangedRecorder(IRoundRepository rounds) : INotificationHandler<RoomSlotLockChanged>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomSlotLockChanged @event, CancellationToken cancellationToken)
	{
		if (@event.RoundProgress is { Completed: true } progress)
			await rounds.CreateOrUpdateAsync(progress.Round, cancellationToken);
	}
}