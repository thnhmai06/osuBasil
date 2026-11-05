using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SetUserAvatar" /> on the account it names.</summary>
internal sealed class SetUserAvatarHandler(UserRegistry registry) : IRequestHandler<SetUserAvatar, Result<UserOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<UserOutcome>> Handle(SetUserAvatar request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Profile
			.SetAvatarAsync(request.Image, cancellationToken);
	}
}