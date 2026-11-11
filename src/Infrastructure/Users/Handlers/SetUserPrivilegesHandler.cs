using Basil.Application.Shared;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SetUserPrivileges" /> on the account it names.</summary>
internal sealed class SetUserPrivilegesHandler(UserRegistry registry)
	: IRequestHandler<SetUserPrivileges, UserOutcome>
{
	/// <inheritdoc />
	public async ValueTask<UserOutcome> Handle(SetUserPrivileges request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Privileges
			.SetAsync(request.Privileges);
	}
}