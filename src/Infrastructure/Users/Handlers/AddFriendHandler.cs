using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class AddFriendHandler(UserRegistry registry) : IRequestHandler<AddFriend, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(AddFriend request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Relationships.AddFriendAsync();
	}
}