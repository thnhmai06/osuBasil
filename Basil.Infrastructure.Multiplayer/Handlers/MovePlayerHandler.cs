using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class MovePlayerHandler : IRequestHandler<MovePlayer, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(MovePlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Slots.MoveAsync(request.Player, request.Index));
	}
}