using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class RelayScoreHandler : IRequestHandler<RelayScore, bool>
{
	public ValueTask<bool> Handle(RelayScore request, CancellationToken ct)
	{
		return ValueTask.FromResult(request.Room.ScopeTo(request.Actor).Rounds.RelayScore(request.Frame));
	}
}
