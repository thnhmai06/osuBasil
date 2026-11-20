using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class SetPlayerTeamHandler : IRequestHandler<SetPlayerTeam, RoomTargetResult>
{
	public ValueTask<RoomTargetResult> Handle(SetPlayerTeam request, CancellationToken ct)
	{
		return new ValueTask<RoomTargetResult>(
			request.Room.ScopeTo(request.Actor).Slots.SetTeamAsync(request.Player, request.Team));
	}
}