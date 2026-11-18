using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class MovePlayerHandler : IRequestHandler<MovePlayer, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(MovePlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Slots.MoveAsync(request.Player, request.Index));
	}
}