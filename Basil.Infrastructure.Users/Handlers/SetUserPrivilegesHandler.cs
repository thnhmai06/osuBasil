using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetUserPrivilegesHandler(UserRegistry registry)
	: IRequestHandler<SetUserPrivileges, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(SetUserPrivileges request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Privileges
			.SetAsync(request.Privileges);
	}
}