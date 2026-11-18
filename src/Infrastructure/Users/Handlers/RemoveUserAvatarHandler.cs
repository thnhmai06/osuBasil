using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class RemoveUserAvatarHandler(UserRegistry registry)
	: IRequestHandler<RemoveUserAvatar, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(RemoveUserAvatar request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Profile.RemoveAvatarAsync();
	}
}