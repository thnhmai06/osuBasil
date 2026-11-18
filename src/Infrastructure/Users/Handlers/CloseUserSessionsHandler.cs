using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class CloseUserSessionsHandler(UserRegistry registry)
	: IRequestHandler<CloseUserSessions, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(CloseUserSessions request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Sessions
			.CloseAllAsync(request.KeepCurrent);
	}
}