using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetUserAwayHandler(UserRegistry registry) : IRequestHandler<SetUserAway, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(SetUserAway request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Profile.SetAwayAsync(request.Message);
	}
}