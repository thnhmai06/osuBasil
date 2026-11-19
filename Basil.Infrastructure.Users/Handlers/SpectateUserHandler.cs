using Basil.Application.Users.Messages;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SpectateUserHandler : IRequestHandler<SpectateUser, SessionOutcome>
{
	public async ValueTask<SessionOutcome> Handle(SpectateUser request, CancellationToken ct)
	{
		return await request.Host.ScopeTo(request.Actor).SpectateAsync();
	}
}