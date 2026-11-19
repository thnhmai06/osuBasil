using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class DeleteMenuBannerHandler(Server server) : IRequestHandler<DeleteMenuBanner, ServerActionResult>
{
	public async ValueTask<ServerActionResult> Handle(DeleteMenuBanner request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Banners.DeleteAsync(request.Banner);
	}
}