using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Registry;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Lists the matches the connection that asked may view.</summary>
internal sealed class ListMatchesHandler(Lobby lobby) : IRequestHandler<ListMatches, MatchListing>
{
	/// <inheritdoc />
	public async ValueTask<MatchListing> Handle(ListMatches request, CancellationToken ct)
	{
		var (result, matches) = await lobby.ScopeTo(request.Actor)
			.ListMatchesAsync(request.Query, request.Page, ct);
		return new MatchListing(result, matches);
	}
}