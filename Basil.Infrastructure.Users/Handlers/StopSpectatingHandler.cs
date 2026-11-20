using Basil.Application.Users.Messages;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class StopSpectatingHandler : IRequestHandler<StopSpectating, bool>
{
	public ValueTask<bool> Handle(StopSpectating request, CancellationToken ct)
	{
		return ValueTask.FromResult(request.Host.ScopeTo(request.Actor).StopSpectating());
	}
}