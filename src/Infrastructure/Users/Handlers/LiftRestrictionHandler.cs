using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="LiftRestriction" /> on the account it names.</summary>
internal sealed class LiftRestrictionHandler(UserRegistry registry)
	: IRequestHandler<LiftRestriction, Result<UserOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<UserOutcome>> Handle(LiftRestriction request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Restrictions
			.LiftAsync(request.Restriction, null, cancellationToken);
	}
}