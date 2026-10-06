using Basil.Domain.Content;

namespace Basil.Application.Storage.Contracts.Content;

/// <summary>Stores the banners shown on the osu! main menu.</summary>
public interface IMenuBannerRepository
{
	/// <summary>Stores a banner, replacing the stored banner with the same image.</summary>
	/// <param name="banner">The banner to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateOrUpdateAsync(MenuBanner banner, CancellationToken cancellationToken = default);

	/// <summary>Gets a banner by its image.</summary>
	/// <param name="image">The image URI to look up.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The banner, or <see langword="null" /> when none shows that image.</returns>
	ValueTask<MenuBanner?> GetAsync(Uri image, CancellationToken cancellationToken = default);

	/// <summary>Lists every banner, whether or not it is shown now.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>All stored banners.</returns>
	Task<IReadOnlyList<MenuBanner>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Deletes a banner; deleting a missing one does nothing.</summary>
	/// <param name="banner">The banner to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(MenuBanner banner, CancellationToken cancellationToken = default);
}