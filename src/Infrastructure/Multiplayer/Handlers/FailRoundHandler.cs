using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class FailRoundHandler : IRequestHandler<FailRound, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(FailRound request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Rounds.FailAsync());
	}
}