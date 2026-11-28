using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Sessions.Registry;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class CheckRegistrationHandler(AccountRegistration registration)
	: IRequestHandler<CheckRegistration, RegistrationFailure?>
{
	public async ValueTask<RegistrationFailure?> Handle(CheckRegistration request, CancellationToken ct)
	{
		return await registration.CheckAsync(request.Attempt, ct);
	}
}
