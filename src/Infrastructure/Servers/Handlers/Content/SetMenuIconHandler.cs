using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class SetMenuIconHandler(Server server) : IRequestHandler<SetMenuIcon, ServerOutcome>
{
	public async ValueTask<ServerOutcome> Handle(SetMenuIcon request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Icon.ReplaceAsync(request.Name, request.Image, request.Url);
	}
}