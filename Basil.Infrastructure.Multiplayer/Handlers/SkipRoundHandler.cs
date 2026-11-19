using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class SkipRoundHandler : IRequestHandler<SkipRound, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(SkipRound request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Rounds.SkipAsync());
	}
}