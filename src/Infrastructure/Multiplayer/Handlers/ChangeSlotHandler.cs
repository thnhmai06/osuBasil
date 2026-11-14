using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="ChangeSlot" /> on the room for the connection that asked.</summary>
internal sealed class ChangeSlotHandler : IRequestHandler<ChangeSlot, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(ChangeSlot request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Slots.ChangeAsync(request.Index));
	}
}