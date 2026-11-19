using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ToggleSlotLockHandler : IRequestHandler<ToggleSlotLock, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(ToggleSlotLock request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Slots.ToggleLockAsync(request.Index));
	}
}