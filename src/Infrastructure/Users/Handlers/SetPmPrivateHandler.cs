using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetPmPrivateHandler : IRequestHandler<SetPmPrivate, bool>
{
	public async ValueTask<bool> Handle(SetPmPrivate request, CancellationToken ct)
	{
		return await request.Actor.User.ScopeTo(request.Actor).Profile.SetPmPrivateAsync(request.PmPrivate) == UserOutcome.Ok;
	}
}