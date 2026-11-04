using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="RestrictUser" /> on the account it names.</summary>
internal sealed class RestrictUserHandler(UserRegistry registry) : IRequestHandler<RestrictUser, (Result<UserOutcome> Result, UserRestriction? Restriction)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<UserOutcome> Result, UserRestriction? Restriction)> Handle(RestrictUser request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Restrictions.RestrictAsync(request.Permissions, request.EndsAt, request.Reason, cancellationToken);
	}
}
