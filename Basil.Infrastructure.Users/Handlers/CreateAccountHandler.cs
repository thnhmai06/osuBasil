using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class CreateAccountHandler(UserRegistry registry)
	: IRequestHandler<CreateAccount, (UserRecord? User, UserOutcome Result)>
{
	public async ValueTask<(UserRecord? User, UserOutcome Result)> Handle(CreateAccount request,
		CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor).CreateAccountAsync(request.Data, request.PasswordHash,
			request.Privileges);
	}
}