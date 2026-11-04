using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="DeleteRestriction" /> on the account it names.</summary>
internal sealed class DeleteRestrictionHandler(UserRegistry registry) : IRequestHandler<DeleteRestriction, Result<UserOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<UserOutcome>> Handle(DeleteRestriction request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Restrictions.DeleteAsync(request.Restriction, null, cancellationToken);
	}
}
