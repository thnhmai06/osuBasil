using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Paging;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class ListScreenshotsHandler(Server server)
	: IRequestHandler<ListScreenshots, (ServerActionResult Result, Page<ContentReference> Screenshots)>
{
	public async ValueTask<(ServerActionResult Result, Page<ContentReference> Screenshots)> Handle(
		ListScreenshots request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Screenshots.ListAsync(request.Page, ct);
	}
}