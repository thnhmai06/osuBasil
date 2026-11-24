using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class MayAnnounceHandler(Server server) : IRequestHandler<MayAnnounce, ServerActionResult>
{
	public ValueTask<ServerActionResult> Handle(MayAnnounce request, CancellationToken ct)
	{
		return ValueTask.FromResult(server.ScopeTo(request.Actor).MayAnnounce());
	}
}
