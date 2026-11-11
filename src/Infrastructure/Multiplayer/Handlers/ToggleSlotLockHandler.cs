using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="ToggleSlotLock" /> on the room for the connection that asked.</summary>
internal sealed class ToggleSlotLockHandler : IRequestHandler<ToggleSlotLock, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(ToggleSlotLock request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Slots.ToggleLockAsync(request.Index));
	}
}