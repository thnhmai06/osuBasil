using Basil.Application.Users.Registry;
using Basil.Application.Users.Messages;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class BlockUserHandler(UserRegistry registry) : IRequestHandler<BlockUser, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(BlockUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Id).Relationships.BlockAsync(request.OtherId);
	}
}