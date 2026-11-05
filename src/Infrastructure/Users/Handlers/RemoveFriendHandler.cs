using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="RemoveFriend" /> on the account it names.</summary>
internal sealed class RemoveFriendHandler(UserRegistry registry) : IRequestHandler<RemoveFriend, Result<UserOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<UserOutcome>> Handle(RemoveFriend request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Relationships
			.RemoveFriendAsync(cancellationToken);
	}
}