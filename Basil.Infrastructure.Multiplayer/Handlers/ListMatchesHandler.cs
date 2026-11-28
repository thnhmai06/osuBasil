using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Registry;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ListMatchesHandler(Lobby lobby) : IRequestHandler<ListMatches, MatchListing>
{
	public async ValueTask<MatchListing> Handle(ListMatches request, CancellationToken ct)
	{
		var (result, matches) = await lobby.ScopeTo(request.Actor)
			.ListMatchesAsync(request.Query, request.Page, ct);
		return new MatchListing(result, matches);
	}
}