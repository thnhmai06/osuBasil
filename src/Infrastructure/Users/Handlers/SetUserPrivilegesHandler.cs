using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetUserPrivilegesHandler(UserRegistry registry)
	: IRequestHandler<SetUserPrivileges, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(SetUserPrivileges request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Privileges
			.SetAsync(request.Privileges);
	}
}