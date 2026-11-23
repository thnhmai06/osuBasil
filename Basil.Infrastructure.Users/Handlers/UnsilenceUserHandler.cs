using Basil.Application.Users.Registry;
using Basil.Application.Users.Messages;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class UnsilenceUserHandler(UserRegistry registry)
	: IRequestHandler<UnsilenceUser, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(UnsilenceUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Silence
			.LiftAsync(request.Reason);
	}
}