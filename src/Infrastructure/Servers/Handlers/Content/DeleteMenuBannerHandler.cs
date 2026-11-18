using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

internal sealed class DeleteMenuBannerHandler(Server server) : IRequestHandler<DeleteMenuBanner, ServerOutcome>
{
	public async ValueTask<ServerOutcome> Handle(DeleteMenuBanner request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Banners.DeleteAsync(request.Banner);
	}
}