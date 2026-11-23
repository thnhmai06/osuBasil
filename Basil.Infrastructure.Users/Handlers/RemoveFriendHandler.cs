using Basil.Application.Users.Registry;
using Basil.Application.Users.Messages;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class RemoveFriendHandler(UserRegistry registry) : IRequestHandler<RemoveFriend, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(RemoveFriend request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Relationships
			.RemoveFriendAsync();
	}
}