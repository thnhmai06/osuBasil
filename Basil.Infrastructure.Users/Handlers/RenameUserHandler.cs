using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class RenameUserHandler(UserRegistry registry) : IRequestHandler<RenameUser, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(RenameUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Profile
			.RenameAsync(request.Name);
	}
}