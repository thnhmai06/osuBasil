using Basil.Application.Shared;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SetUserAvatar" /> on the account it names.</summary>
internal sealed class SetUserAvatarHandler(UserRegistry registry) : IRequestHandler<SetUserAvatar, UserOutcome>
{
	/// <inheritdoc />
	public async ValueTask<UserOutcome> Handle(SetUserAvatar request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Profile
			.SetAvatarAsync(request.Image, ct);
	}
}