using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using Basil.Domain.Sessions;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class GetUserSessionHandler(SessionRegistry registry)
	: IRequestHandler<GetUserSession, (UserActionResult Result, SessionRecord? Session)>
{
	public async ValueTask<(UserActionResult Result, SessionRecord? Session)> Handle(GetUserSession request,
		CancellationToken ct)
	{
		return await registry.ScopeSessions(request.Actor, request.TargetId)
			.GetAsync(request.SessionId, ct);
	}
}