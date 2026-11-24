using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class LoginSessionHandler(SessionRegistry registry) : IRequestHandler<LoginSession, LoginResult>
{
	public async ValueTask<LoginResult> Handle(LoginSession request, CancellationToken ct)
	{
		return await registry.LoginAsync(request.Attempt, request.Type, request.Ip, request.Client, request.UtcOffset,
			ct);
	}
}
