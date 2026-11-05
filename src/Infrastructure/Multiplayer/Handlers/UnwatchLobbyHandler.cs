using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Registry;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Carries out <see cref="UnwatchLobby" /> for the osu! client that asked.</summary>
internal sealed class UnwatchLobbyHandler(Lobby lobby) : IRequestHandler<UnwatchLobby>
{
	/// <inheritdoc />
	public ValueTask<Unit> Handle(UnwatchLobby request, CancellationToken cancellationToken)
	{
		lobby.ScopeTo(request.Actor).Unwatch();
		return ValueTask.FromResult(Unit.Value);
	}
}