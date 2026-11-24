using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class MayChangeMenuHandler(Server server) : IRequestHandler<MayChangeMenu, ServerActionResult>
{
	public ValueTask<ServerActionResult> Handle(MayChangeMenu request, CancellationToken ct)
	{
		return ValueTask.FromResult(server.ScopeTo(request.Actor).MayChangeMenu());
	}
}
