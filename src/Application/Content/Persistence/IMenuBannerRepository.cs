using Basil.Application.Shared.Paging;
using Basil.Domain.Content;

namespace Basil.Application.Content.Persistence;

/// <summary>Stores the banners shown on the osu! main menu.</summary>
public interface IMenuBannerRepository
{
	/// <summary>Stores a banner, replacing the stored banner with the same image.</summary>
	/// <param name="banner">The banner to store.</param>
	Task CreateOrUpdateAsync(MenuBanner banner);

	/// <summary>Gets a banner by its image.</summary>
	/// <param name="image">The image to look up.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The banner, or <see langword="null" /> when none shows that image.</returns>
	ValueTask<MenuBanner?> GetAsync(ContentReference image, CancellationToken ct = default);

	/// <summary>Lists every banner, whether or not it is shown now, oldest first.</summary>
	/// <remarks>At most <see cref="ListLimits.Ceiling" /> banners are returned.</remarks>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>All stored banners.</returns>
	Task<IEnumerable<MenuBanner>> ListAsync(CancellationToken ct = default);

	/// <summary>Deletes a banner; deleting a missing one does nothing.</summary>
	/// <param name="banner">The banner to delete.</param>
	Task DeleteAsync(MenuBanner banner);
}