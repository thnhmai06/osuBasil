using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Sessions;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class GetUserSessionHandler(UserRegistry registry)
	: IRequestHandler<GetUserSession, (UserActionResult Result, SessionRecord? Session)>
{
	public async ValueTask<(UserActionResult Result, SessionRecord? Session)> Handle(GetUserSession request,
		CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Sessions
			.GetAsync(request.SessionId, ct);
	}
}