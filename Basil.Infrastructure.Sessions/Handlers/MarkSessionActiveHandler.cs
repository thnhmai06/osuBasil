using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class MarkSessionActiveHandler(SessionRegistry registry) : IRequestHandler<MarkSessionActive>
{
	public ValueTask<Unit> Handle(MarkSessionActive request, CancellationToken ct)
	{
		registry.MarkActive(request.Session);
		return Unit.ValueTask;
	}
}
