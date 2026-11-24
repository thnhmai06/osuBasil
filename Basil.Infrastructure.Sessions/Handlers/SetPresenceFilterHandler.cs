using Basil.Application.Sessions.Messages;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class SetPresenceFilterHandler : IRequestHandler<SetPresenceFilter, bool>
{
	public ValueTask<bool> Handle(SetPresenceFilter request, CancellationToken ct)
	{
		return ValueTask.FromResult(request.Actor.ScopeTo(request.Actor).SetPresenceFilter(request.Filter));
	}
}
