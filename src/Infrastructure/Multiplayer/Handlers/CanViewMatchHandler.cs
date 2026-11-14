using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Registry;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Answers whether the connection that asked may view a match.</summary>
internal sealed class CanViewMatchHandler(Lobby lobby, IMatchRepository matches)
	: IRequestHandler<CanViewMatch, bool>
{
	/// <inheritdoc />
	public async ValueTask<bool> Handle(CanViewMatch request, CancellationToken ct)
	{
		var match = lobby.Find(request.MatchId)?.Match ?? await matches.GetAsync(request.MatchId, ct);
		return match is not null && await lobby.ScopeTo(request.Actor).CanViewAsync(match, ct);
	}
}