using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ToggleSlotLockHandler : IRequestHandler<ToggleSlotLock, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(ToggleSlotLock request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Slots.ToggleLockAsync(request.Index));
	}
}