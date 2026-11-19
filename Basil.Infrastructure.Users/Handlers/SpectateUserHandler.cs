using Basil.Application.Users.Messages;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SpectateUserHandler : IRequestHandler<SpectateUser, SessionAuthResult>
{
	public async ValueTask<SessionAuthResult> Handle(SpectateUser request, CancellationToken ct)
	{
		return await request.Host.ScopeTo(request.Actor).SpectateAsync();
	}
}