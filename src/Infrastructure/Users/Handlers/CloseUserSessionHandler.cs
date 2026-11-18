using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class CloseUserSessionHandler(UserRegistry registry)
	: IRequestHandler<CloseUserSession, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(CloseUserSession request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Sessions
			.CloseAsync(request.SessionId);
	}
}