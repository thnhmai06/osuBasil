using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetPmPrivateHandler(UserRegistry registry) : IRequestHandler<SetPmPrivate, bool>
{
	public async ValueTask<bool> Handle(SetPmPrivate request, CancellationToken ct)
	{
		return await registry.Of(request.Actor.Record).ScopeTo(request.Actor).Profile.SetPmPrivateAsync(request.PmPrivate) ==
		       UserActionResult.Ok;
	}
}