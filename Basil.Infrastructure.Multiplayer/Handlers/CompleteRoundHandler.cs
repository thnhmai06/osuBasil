using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class CompleteRoundHandler : IRequestHandler<CompleteRound, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(CompleteRound request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Rounds.CompleteAsync());
	}
}