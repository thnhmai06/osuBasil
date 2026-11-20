using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class MovePlayerHandler : IRequestHandler<MovePlayer, RoomTargetResult>
{
	public ValueTask<RoomTargetResult> Handle(MovePlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomTargetResult>(
			request.Room.ScopeTo(request.Actor).Slots.MoveAsync(request.PlayerId, request.Index));
	}
}