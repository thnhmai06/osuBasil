using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class SetPlayerTeamHandler : IRequestHandler<SetPlayerTeam, LobbyResult>
{
	public ValueTask<LobbyResult> Handle(SetPlayerTeam request, CancellationToken ct)
	{
		return new ValueTask<LobbyResult>(
			request.Room.ScopeTo(request.Actor).Slots.SetTeamAsync(request.Player, request.Team));
	}
}