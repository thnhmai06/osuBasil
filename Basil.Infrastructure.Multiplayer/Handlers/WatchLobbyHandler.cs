using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Registry;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class WatchLobbyHandler(Lobby lobby) : IRequestHandler<WatchLobby>
{
	public ValueTask<Unit> Handle(WatchLobby request, CancellationToken ct)
	{
		lobby.ScopeTo(request.Actor).Watch();
		return ValueTask.FromResult(Unit.Value);
	}
}