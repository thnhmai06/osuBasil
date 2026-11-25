using Basil.Application.Sessions.Messages;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class RelaySpectatorFramesHandler : IRequestHandler<RelaySpectatorFrames, SpectatorRelay>
{
	public ValueTask<SpectatorRelay> Handle(RelaySpectatorFrames request, CancellationToken ct)
	{
		return ValueTask.FromResult(request.Actor.ScopeTo(request.Actor).Relay());
	}
}