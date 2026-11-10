using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="RemoveUserAvatar" /> on the account it names.</summary>
internal sealed class RemoveUserAvatarHandler(UserRegistry registry)
	: IRequestHandler<RemoveUserAvatar, Result<UserOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<UserOutcome>> Handle(RemoveUserAvatar request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Profile.RemoveAvatarAsync();
	}
}