using Basil.Application.Users.Messages;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="PublishSpectatorFrames" /> for the osu! client that played the frames.</summary>
internal sealed class PublishSpectatorFramesHandler : IRequestHandler<PublishSpectatorFrames, bool>
{
	/// <inheritdoc />
	public ValueTask<bool> Handle(PublishSpectatorFrames request, CancellationToken cancellationToken)
	{
		return ValueTask.FromResult(request.Actor.ScopeTo(request.Actor).PublishFrames(request.Bundle));
	}
}
