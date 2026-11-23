using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class CreateAccountHandler(SessionRegistry registry)
	: IRequestHandler<CreateAccount, (UserRecord? User, UserActionResult Result)>
{
	public async ValueTask<(UserRecord? User, UserActionResult Result)> Handle(CreateAccount request,
		CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor).CreateAccountAsync(request.Data, request.PasswordHash,
			request.Privileges);
	}
}