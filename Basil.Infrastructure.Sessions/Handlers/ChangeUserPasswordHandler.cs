using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class ChangeUserPasswordHandler(SessionRegistry registry)
	: IRequestHandler<ChangeUserPassword, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(ChangeUserPassword request, CancellationToken ct)
	{
		return await registry.ScopeCredentials(request.Actor, request.TargetId)
			.ChangePasswordAsync(request.NewPasswordHash, request.CurrentPasswordHash, request.IncludeCurrent);
	}
}