using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class MovePlayerHandler : IRequestHandler<MovePlayer, LobbyResult>
{
	public ValueTask<LobbyResult> Handle(MovePlayer request, CancellationToken ct)
	{
		return new ValueTask<LobbyResult>(
			request.Room.ScopeTo(request.Actor).Slots.MoveAsync(request.Player, request.Index));
	}
}