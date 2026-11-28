using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class CheckRoundStartHandler : IRequestHandler<CheckRoundStart, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(CheckRoundStart request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Rounds.RequireStart() ?? RoomActionResult.Ok);
	}
}