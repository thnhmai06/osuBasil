using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ChangeSlotHandler : IRequestHandler<ChangeSlot, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(ChangeSlot request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Slots.ChangeAsync(request.Index));
	}
}