using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class CloseSessionHandler(SessionRegistry registry) : IRequestHandler<CloseSession>
{
	public ValueTask<Unit> Handle(CloseSession request, CancellationToken ct)
	{
		registry.Close(request.Session, request.Reason);
		return Unit.ValueTask;
	}
}
