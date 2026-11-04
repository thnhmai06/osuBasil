using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Registry;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Carries out <see cref="WatchLobby" /> for the osu! client that asked.</summary>
internal sealed class WatchLobbyHandler(Lobby lobby) : IRequestHandler<WatchLobby>
{
	/// <inheritdoc />
	public ValueTask<Unit> Handle(WatchLobby request, CancellationToken cancellationToken)
	{
		lobby.ScopeTo(request.Actor).Watch();
		return ValueTask.FromResult(Unit.Value);
	}
}
