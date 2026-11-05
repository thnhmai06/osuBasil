using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="CloseUserSessions" /> on the account it names.</summary>
internal sealed class CloseUserSessionsHandler(UserRegistry registry)
	: IRequestHandler<CloseUserSessions, Result<UserOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<UserOutcome>> Handle(CloseUserSessions request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Sessions
			.CloseAllAsync(request.KeepCurrent, cancellationToken);
	}
}