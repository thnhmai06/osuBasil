using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class UnsilenceUserHandler(UserRegistry registry)
	: IRequestHandler<UnsilenceUser, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(UnsilenceUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Silence
			.LiftAsync(request.Reason);
	}
}