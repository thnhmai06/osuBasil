using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SetUserPrivileges" /> on the account it names.</summary>
internal sealed class SetUserPrivilegesHandler(UserRegistry registry)
	: IRequestHandler<SetUserPrivileges, Result<UserOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<UserOutcome>> Handle(SetUserPrivileges request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Privileges
			.SetAsync(request.Privileges, cancellationToken);
	}
}