using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class CheckRoundStartHandler : IRequestHandler<CheckRoundStart, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(CheckRoundStart request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Rounds.RequireStart() ?? RoomOutcome.Ok);
	}
}