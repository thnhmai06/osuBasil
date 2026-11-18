using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class BlockUserHandler(UserRegistry registry) : IRequestHandler<BlockUser, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(BlockUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Relationships.BlockAsync();
	}
}