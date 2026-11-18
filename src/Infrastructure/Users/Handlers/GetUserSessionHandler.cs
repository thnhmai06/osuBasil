using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class GetUserSessionHandler(UserRegistry registry)
	: IRequestHandler<GetUserSession, (UserOutcome Result, Basil.Domain.Authentication.Session? Session)>
{
	public async ValueTask<(UserOutcome Result, Basil.Domain.Authentication.Session? Session)> Handle(GetUserSession request,
		CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Sessions
			.GetAsync(request.SessionId, ct);
	}
}