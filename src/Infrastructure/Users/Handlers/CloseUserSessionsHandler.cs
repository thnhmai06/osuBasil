using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="CloseUserSessions" /> on the account it names.</summary>
internal sealed class CloseUserSessionsHandler(UserRegistry registry)
	: IRequestHandler<CloseUserSessions, UserOutcome>
{
	/// <inheritdoc />
	public async ValueTask<UserOutcome> Handle(CloseUserSessions request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Sessions
			.CloseAllAsync(request.KeepCurrent);
	}
}