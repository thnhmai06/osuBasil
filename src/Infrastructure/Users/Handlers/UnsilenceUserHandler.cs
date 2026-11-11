using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="UnsilenceUser" /> on the account it names.</summary>
internal sealed class UnsilenceUserHandler(UserRegistry registry)
	: IRequestHandler<UnsilenceUser, UserOutcome>
{
	/// <inheritdoc />
	public async ValueTask<UserOutcome> Handle(UnsilenceUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Silence
			.LiftAsync(request.Reason);
	}
}