using Basil.Application.Content.Messages;
using Basil.Application.Content.Persistence;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Content.Handlers;

internal sealed class ListBannersHandler(IMenuBannerRepository banners)
	: IRequestHandler<ListBanners, IReadOnlyList<MenuBanner>>
{
	public async ValueTask<IReadOnlyList<MenuBanner>> Handle(ListBanners request, CancellationToken ct)
	{
		return [.. await banners.ListAsync(ct)];
	}
}