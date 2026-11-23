using Basil.Application.Users.Registry;
using Basil.Application.Users.Messages;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class UnblockUserHandler(UserRegistry registry) : IRequestHandler<UnblockUser, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(UnblockUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Relationships.UnblockAsync();
	}
}