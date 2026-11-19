using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class StartRoundHandler : IRequestHandler<StartRound, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(StartRound request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Rounds.StartAsync());
	}
}