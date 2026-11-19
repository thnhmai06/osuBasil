using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class ChangeUserPasswordHandler(UserRegistry registry)
	: IRequestHandler<ChangeUserPassword, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(ChangeUserPassword request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Credentials
			.ChangePasswordAsync(request.NewPasswordHash, request.CurrentPasswordHash, request.IncludeCurrent);
	}
}