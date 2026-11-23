using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class CloseUserSessionHandler(SessionRegistry registry)
	: IRequestHandler<CloseUserSession, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(CloseUserSession request, CancellationToken ct)
	{
		return await registry.ScopeSessions(request.Actor, request.TargetId)
			.CloseAsync(request.SessionId);
	}
}