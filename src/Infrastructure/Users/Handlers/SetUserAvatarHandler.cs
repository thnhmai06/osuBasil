using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetUserAvatarHandler(UserRegistry registry) : IRequestHandler<SetUserAvatar, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(SetUserAvatar request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Profile
			.SetAvatarAsync(request.Image, ct);
	}
}