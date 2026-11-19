using Basil.Application.Shared.Paging;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class ListUserSessionsHandler(UserRegistry registry)
	: IRequestHandler<ListUserSessions, (UserActionResult Result, Page<SessionRecord>? Sessions)>
{
	public async ValueTask<(UserActionResult Result, Page<SessionRecord>? Sessions)> Handle(
		ListUserSessions request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Sessions
			.ListAsync(request.Page, request.State, request.Types, ct);
	}
}