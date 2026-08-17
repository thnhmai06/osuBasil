using Basil.Domain.Content;

namespace Basil.Application.Abstractions.Content;

/// <summary>
///     Provides CRUD access to the MenuBanners table.
/// </summary>
public interface IMenuBannerRepository
{
	/// <summary>Fetches every stored banner, ordered by <see cref="MenuBanner.CreatedAt" />.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task<IReadOnlyList<MenuBanner>> FetchAllAsync(CancellationToken cancellationToken = default);

	/// <summary>Fetches a single banner by id.</summary>
	/// <param name="id">The banner's id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The matching banner, or <see langword="null" /> when no such banner exists.</returns>
	Task<MenuBanner?> FetchByIdAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Creates a new banner.</summary>
	/// <param name="image">A locally stored filename or an external URL.</param>
	/// <param name="url">The click-through URL.</param>
	/// <param name="begins">The UTC instant the banner starts being current.</param>
	/// <param name="expires">The UTC instant the banner stops being current.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The created banner, with its assigned id.</returns>
	Task<MenuBanner> CreateAsync(string image, string url, DateTime begins, DateTime expires,
		CancellationToken cancellationToken = default);

	/// <summary>Updates an existing banner's fields, leaving any <see langword="null" /> argument unchanged.</summary>
	/// <param name="id">The banner's id.</param>
	/// <param name="image">The new image value, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="url">The new click-through URL, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="begins">The new start instant, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="expires">The new end instant, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The updated banner, or <see langword="null" /> when no such banner exists.</returns>
	Task<MenuBanner?> UpdateAsync(int id, string? image, string? url, DateTime? begins, DateTime? expires,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a banner.</summary>
	/// <param name="id">The banner's id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if a banner was deleted; otherwise, <see langword="false" />.</returns>
	Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
