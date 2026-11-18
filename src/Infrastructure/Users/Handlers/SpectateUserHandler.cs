using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SpectateUserHandler : IRequestHandler<SpectateUser, ConnectionOutcome>
{
	public async ValueTask<ConnectionOutcome> Handle(SpectateUser request, CancellationToken ct)
	{
		return await request.Host.ScopeTo(request.Actor).SpectateAsync();
	}
}