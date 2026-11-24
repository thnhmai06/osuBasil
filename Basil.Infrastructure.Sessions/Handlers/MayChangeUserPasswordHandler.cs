using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class MayChangeUserPasswordHandler(SessionRegistry registry)
	: IRequestHandler<MayChangeUserPassword, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(MayChangeUserPassword request, CancellationToken ct)
	{
		return await registry.ScopeCredentials(request.Actor, request.TargetId).MayChangePasswordAsync();
	}
}
