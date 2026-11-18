using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;
using Basil.Domain.Multiplayer;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class SkipRoundHandler : IRequestHandler<SkipRound, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(SkipRound request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Rounds.SkipAsync());
	}
}