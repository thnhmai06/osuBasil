using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetUserAwayHandler(UserRegistry registry) : IRequestHandler<SetUserAway, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(SetUserAway request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Profile.SetAwayAsync(request.Message);
	}
}