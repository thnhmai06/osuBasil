using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Registry;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class CanViewMatchHandler(Lobby lobby, IMatchRepository matches)
	: IRequestHandler<CanViewMatch, bool>
{
	public async ValueTask<bool> Handle(CanViewMatch request, CancellationToken ct)
	{
		var match = lobby.Find(request.MatchId)?.Match ?? await matches.GetAsync(request.MatchId, ct);
		return match is not null && await lobby.ScopeTo(request.Actor).CanViewAsync(match, ct);
	}
}