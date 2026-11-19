using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class RemoveUserAvatarHandler(UserRegistry registry)
	: IRequestHandler<RemoveUserAvatar, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(RemoveUserAvatar request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Profile.RemoveAvatarAsync();
	}
}