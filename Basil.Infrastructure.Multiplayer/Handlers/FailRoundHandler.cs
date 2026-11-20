using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class FailRoundHandler : IRequestHandler<FailRound, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(FailRound request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Rounds.FailAsync());
	}
}