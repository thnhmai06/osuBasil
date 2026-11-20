using Basil.Application.Users.Messages;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class PublishSpectatorFramesHandler : IRequestHandler<PublishSpectatorFrames, bool>
{
	public ValueTask<bool> Handle(PublishSpectatorFrames request, CancellationToken ct)
	{
		return ValueTask.FromResult(request.Actor.ScopeTo(request.Actor).PublishFrames(request.Bundle));
	}
}