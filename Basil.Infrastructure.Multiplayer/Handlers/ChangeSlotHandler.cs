using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ChangeSlotHandler : IRequestHandler<ChangeSlot, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(ChangeSlot request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Slots.ChangeAsync(request.Index));
	}
}