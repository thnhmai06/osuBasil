using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class UnsilenceUserHandler(UserRegistry registry)
	: IRequestHandler<UnsilenceUser, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(UnsilenceUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Silence
			.LiftAsync(request.Reason);
	}
}