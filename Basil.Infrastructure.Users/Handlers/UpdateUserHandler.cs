using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class UpdateUserHandler(UserRegistry registry) : IRequestHandler<UpdateUser, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(UpdateUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).UpdateAsync(request.Change);
	}
}
