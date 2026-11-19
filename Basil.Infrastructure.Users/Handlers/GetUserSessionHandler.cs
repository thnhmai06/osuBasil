using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class GetUserSessionHandler(UserRegistry registry)
	: IRequestHandler<GetUserSession, (UserOutcome Result, SessionRecord? Session)>
{
	public async ValueTask<(UserOutcome Result, SessionRecord? Session)> Handle(GetUserSession request,
		CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Sessions
			.GetAsync(request.SessionId, ct);
	}
}