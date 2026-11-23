using Basil.Application.Sessions.Messages;
using Basil.Domain.Sessions;
using Mediator;
using Basil.Application.Sessions.Objects;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class SpectateUserHandler : IRequestHandler<SpectateUser, SessionAuthResult>
{
	public async ValueTask<SessionAuthResult> Handle(SpectateUser request, CancellationToken ct)
	{
		return await request.Host.ScopeTo(request.Actor).SpectateAsync();
	}
}