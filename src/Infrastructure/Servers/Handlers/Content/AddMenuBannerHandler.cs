using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class AddMenuBannerHandler(Server server)
	: IRequestHandler<AddMenuBanner, (ServerOutcome Result, MenuBanner? Banner)>
{
	public async ValueTask<(ServerOutcome Result, MenuBanner? Banner)> Handle(AddMenuBanner request,
		CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Banners.CreateAsync(request.Name, request.Image, request.Url,
			request.StartsAt, request.EndsAt);
	}
}