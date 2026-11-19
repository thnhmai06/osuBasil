using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class AbortRoundHandler : IRequestHandler<AbortRound, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(AbortRound request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Rounds.AbortAsync());
	}
}