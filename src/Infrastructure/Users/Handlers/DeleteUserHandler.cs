using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class DeleteUserHandler(UserRegistry registry) : IRequestHandler<DeleteUser, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(DeleteUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).DeleteAsync();
	}
}