using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class CloseUserSessionsHandler(UserRegistry registry)
	: IRequestHandler<CloseUserSessions, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(CloseUserSessions request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Sessions
			.CloseAllAsync(request.KeepCurrent);
	}
}