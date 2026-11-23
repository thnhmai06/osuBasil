using Basil.Application.Users.Registry;
using Basil.Application.Users.Messages;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SilenceUserHandler(UserRegistry registry) : IRequestHandler<SilenceUser, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(SilenceUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Silence
			.ImposeAsync(request.EndsAt, request.Reason);
	}
}