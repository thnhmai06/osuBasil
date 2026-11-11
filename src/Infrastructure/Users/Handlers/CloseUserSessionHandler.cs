using Basil.Application.Shared;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="CloseUserSession" /> on the account it names.</summary>
internal sealed class CloseUserSessionHandler(UserRegistry registry)
	: IRequestHandler<CloseUserSession, UserOutcome>
{
	/// <inheritdoc />
	public async ValueTask<UserOutcome> Handle(CloseUserSession request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Sessions
			.CloseAsync(request.SessionId);
	}
}