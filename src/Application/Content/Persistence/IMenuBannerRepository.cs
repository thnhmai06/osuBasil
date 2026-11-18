using Basil.Application.Shared.Paging;
using Basil.Domain.Content;

namespace Basil.Application.Content.Persistence;

public interface IMenuBannerRepository
{
	Task CreateOrUpdateAsync(MenuBanner banner);

	ValueTask<MenuBanner?> GetAsync(ContentReference image, CancellationToken ct = default);

	Task<IEnumerable<MenuBanner>> ListAsync(CancellationToken ct = default);

	Task DeleteAsync(MenuBanner banner);
}