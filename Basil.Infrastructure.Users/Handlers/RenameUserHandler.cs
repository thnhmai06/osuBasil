using Basil.Application.Users.Registry;
using Basil.Application.Users.Messages;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class RenameUserHandler(UserRegistry registry) : IRequestHandler<RenameUser, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(RenameUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Profile
			.RenameAsync(request.Name);
	}
}