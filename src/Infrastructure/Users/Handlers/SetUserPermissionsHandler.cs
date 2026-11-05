using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SetUserPermissions" /> on the account it names.</summary>
internal sealed class SetUserPermissionsHandler(UserRegistry registry)
	: IRequestHandler<SetUserPermissions, Result<UserOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<UserOutcome>> Handle(SetUserPermissions request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Permissions
			.SetAsync(request.Permissions, cancellationToken);
	}
}