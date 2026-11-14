using Basil.Application.Content.Messages;
using Basil.Application.Content.Persistence;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Content.Handlers;

/// <summary>Answers <see cref="ListBanners" /> from the stored banners.</summary>
internal sealed class ListBannersHandler(IMenuBannerRepository banners)
	: IRequestHandler<ListBanners, IReadOnlyList<MenuBanner>>
{
	/// <inheritdoc />
	public async ValueTask<IReadOnlyList<MenuBanner>> Handle(ListBanners request, CancellationToken ct)
	{
		return [.. await banners.ListAsync(ct)];
	}
}