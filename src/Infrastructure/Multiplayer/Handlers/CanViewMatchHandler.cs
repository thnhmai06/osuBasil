using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Registry;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Answers whether the connection that asked may view a match.</summary>
internal sealed class CanViewMatchHandler(Lobby lobby) : IRequestHandler<CanViewMatch, bool>
{
	/// <inheritdoc />
	public async ValueTask<bool> Handle(CanViewMatch request, CancellationToken cancellationToken)
	{
		return await lobby.ScopeTo(request.Actor).CanViewAsync(request.Match, cancellationToken);
	}
}