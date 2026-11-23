using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class CloseUserSessionsHandler(SessionRegistry registry)
	: IRequestHandler<CloseUserSessions, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(CloseUserSessions request, CancellationToken ct)
	{
		return await registry.ScopeSessions(request.Actor, request.TargetId)
			.CloseAllAsync(request.KeepCurrent);
	}
}