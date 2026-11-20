using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class CloseUserSessionHandler(UserRegistry registry)
	: IRequestHandler<CloseUserSession, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(CloseUserSession request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Sessions
			.CloseAsync(request.SessionId);
	}
}