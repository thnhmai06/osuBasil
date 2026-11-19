using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class ClearMenuIconHandler(Server server) : IRequestHandler<ClearMenuIcon, ServerOutcome>
{
	public async ValueTask<ServerOutcome> Handle(ClearMenuIcon request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Icon.DeleteAsync();
	}
}