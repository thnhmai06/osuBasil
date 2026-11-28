using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Sessions.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class RegisterAccountHandler(AccountRegistration registration)
	: IRequestHandler<RegisterAccount, (UserRecord? User, RegistrationFailure? Failure)>
{
	public async ValueTask<(UserRecord? User, RegistrationFailure? Failure)> Handle(RegisterAccount request,
		CancellationToken ct)
	{
		return await registration.RegisterAsync(request.Attempt, ct);
	}
}
