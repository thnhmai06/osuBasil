using Basil.Application.Users.Messages;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SetPlayerStatus" /> for the osu! client that reported.</summary>
internal sealed class SetPlayerStatusHandler : IRequestHandler<SetPlayerStatus, bool>
{
	/// <inheritdoc />
	public ValueTask<bool> Handle(SetPlayerStatus request, CancellationToken cancellationToken)
	{
		return ValueTask.FromResult(request.Actor.ScopeTo(request.Actor).SetStatus(request.Status));
	}
}