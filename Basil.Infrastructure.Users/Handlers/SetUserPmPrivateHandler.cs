using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetUserPmPrivateHandler(UserRegistry registry) : IRequestHandler<SetUserPmPrivate, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(SetUserPmPrivate request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Profile.SetPmPrivateAsync(request.PmPrivate);
	}
}