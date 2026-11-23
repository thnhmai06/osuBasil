using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using Basil.Application.Shared.Paging;
using Basil.Domain.Sessions;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class ListUserSessionsHandler(SessionRegistry registry)
	: IRequestHandler<ListUserSessions, (UserActionResult Result, Page<SessionRecord>? Sessions)>
{
	public async ValueTask<(UserActionResult Result, Page<SessionRecord>? Sessions)> Handle(
		ListUserSessions request, CancellationToken ct)
	{
		return await registry.ScopeSessions(request.Actor, request.TargetId)
			.ListAsync(request.Page, request.State, request.Types, ct);
	}
}