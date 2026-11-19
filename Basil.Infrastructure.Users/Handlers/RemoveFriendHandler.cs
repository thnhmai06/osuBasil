using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class RemoveFriendHandler(UserRegistry registry) : IRequestHandler<RemoveFriend, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(RemoveFriend request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Relationships
			.RemoveFriendAsync();
	}
}