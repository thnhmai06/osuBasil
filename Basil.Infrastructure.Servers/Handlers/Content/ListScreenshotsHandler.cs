using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class ListScreenshotsHandler(Server server)
	: IRequestHandler<ListScreenshots, (ServerOutcome Result, IReadOnlyList<ContentReference> Screenshots)>
{
	public async ValueTask<(ServerOutcome Result, IReadOnlyList<ContentReference> Screenshots)> Handle(
		ListScreenshots request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Screenshots.ListAsync(ct);
	}
}