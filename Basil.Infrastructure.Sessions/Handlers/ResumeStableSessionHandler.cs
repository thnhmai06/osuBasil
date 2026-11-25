using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class ResumeStableSessionHandler(SessionRegistry registry) : IRequestHandler<ResumeStableSession, Session?>
{
	public ValueTask<Session?> Handle(ResumeStableSession request, CancellationToken ct)
	{
		return ValueTask.FromResult(registry.ResumeStable(request.Token));
	}
}
