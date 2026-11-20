using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class AddMenuBannerHandler(Server server)
	: IRequestHandler<AddMenuBanner, (ServerActionResult Result, MenuBanner? Banner)>
{
	public async ValueTask<(ServerActionResult Result, MenuBanner? Banner)> Handle(AddMenuBanner request,
		CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Banners.CreateAsync(request.Name, request.Image, request.Url,
			request.StartsAt, request.EndsAt);
	}
}