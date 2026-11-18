using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;
using Basil.Domain.Multiplayer;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class CompleteRoundHandler : IRequestHandler<CompleteRound, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(CompleteRound request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Rounds.CompleteAsync());
	}
}