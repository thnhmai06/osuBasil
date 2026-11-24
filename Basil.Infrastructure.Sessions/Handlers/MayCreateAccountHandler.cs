using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class MayCreateAccountHandler(SessionRegistry registry)
	: IRequestHandler<MayCreateAccount, UserActionResult>
{
	public ValueTask<UserActionResult> Handle(MayCreateAccount request, CancellationToken ct)
	{
		return ValueTask.FromResult(registry.ScopeTo(request.Actor).MayCreateAccount());
	}
}
