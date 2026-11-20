using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ToggleTeamHandler : IRequestHandler<ToggleTeam, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(ToggleTeam request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Slots.ToggleTeamAsync());
	}
}