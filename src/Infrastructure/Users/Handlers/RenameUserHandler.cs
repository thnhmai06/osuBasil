using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class RenameUserHandler(UserRegistry registry) : IRequestHandler<RenameUser, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(RenameUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Profile
			.RenameAsync(request.Name);
	}
}