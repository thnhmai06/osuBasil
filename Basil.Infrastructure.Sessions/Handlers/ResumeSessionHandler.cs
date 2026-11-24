using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class ResumeSessionHandler(SessionRegistry registry) : IRequestHandler<ResumeSession, LoginResult>
{
	public async ValueTask<LoginResult> Handle(ResumeSession request, CancellationToken ct)
	{
		return await registry.ResumeAsync(request.Token, request.Accepts, ct);
	}
}
