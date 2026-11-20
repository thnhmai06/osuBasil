using Basil.Application.Users.Messages;
using Basil.Domain.Sessions;
using Mediator;
using Basil.Application.Users.Objects.Sessions;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SpectateUserHandler : IRequestHandler<SpectateUser, SessionAuthResult>
{
	public async ValueTask<SessionAuthResult> Handle(SpectateUser request, CancellationToken ct)
	{
		return await request.Host.ScopeTo(request.Actor).SpectateAsync();
	}
}