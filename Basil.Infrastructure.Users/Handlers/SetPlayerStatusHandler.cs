using Basil.Application.Users.Messages;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetPlayerStatusHandler : IRequestHandler<SetPlayerStatus, bool>
{
	public ValueTask<bool> Handle(SetPlayerStatus request, CancellationToken ct)
	{
		return ValueTask.FromResult(request.Actor.ScopeTo(request.Actor).SetStatus(request.Status));
	}
}