using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="UnblockUser" /> on the account it names.</summary>
internal sealed class UnblockUserHandler(UserRegistry registry) : IRequestHandler<UnblockUser, Result<UserOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<UserOutcome>> Handle(UnblockUser request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Relationships.UnblockAsync(cancellationToken);
	}
}
