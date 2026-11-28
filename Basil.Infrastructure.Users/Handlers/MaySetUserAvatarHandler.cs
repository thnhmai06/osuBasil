using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class MaySetUserAvatarHandler(UserRegistry registry)
	: IRequestHandler<MaySetUserAvatar, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(MaySetUserAvatar request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Profile.MaySetAvatarAsync();
	}
}
