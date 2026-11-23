using Basil.Application.Users.Registry;
using Basil.Application.Users.Messages;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetUserAvatarHandler(UserRegistry registry) : IRequestHandler<SetUserAvatar, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(SetUserAvatar request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Profile
			.SetAvatarAsync(request.Image, ct);
	}
}