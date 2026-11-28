using Basil.Application.Content.Messages;
using Basil.Application.Content.Persistence;
using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Content.Handlers;

internal sealed class ListBannersHandler(IMenuBannerRepository banners, IMenuBannerStorage storage)
	: IRequestHandler<ListBanners, IReadOnlyList<MenuBanner>>
{
	public async ValueTask<IReadOnlyList<MenuBanner>> Handle(ListBanners request, CancellationToken ct)
	{
		var all = await banners.ListAsync(ct);
		if (!request.OnlyAvailable) return [.. all];

		var stored = (await storage.ListAsync(ct)).ToHashSet();
		return [.. all.Where(banner => !banner.Image.IsOffline || stored.Contains(banner.Image))];
	}
}