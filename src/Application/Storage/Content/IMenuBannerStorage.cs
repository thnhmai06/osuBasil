using Basil.Domain.Content;

namespace Basil.Application.Storage.Content;

/// <summary>Stores the images of main-menu banners.</summary>
public interface IMenuBannerStorage
{
	/// <summary>Lists the stored images.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>References to the stored images.</returns>
	Task<IEnumerable<ContentReference>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Stores an image under a name, replacing any image with that name.</summary>
	/// <param name="name">The name to store the image under.</param>
	/// <param name="content">The image bytes.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The reference to the stored image.</returns>
	/// <exception cref="ArgumentException"><paramref name="name" /> is not a safe file name.</exception>
	Task<ContentReference> SaveAsync(string name, Stream content, CancellationToken cancellationToken = default);

	/// <summary>Opens a stored image.</summary>
	/// <param name="image">The reference of the image to open.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The image bytes, or <see langword="null" /> when the reference names no image stored here.</returns>
	Task<Stream?> OpenAsync(ContentReference image, CancellationToken cancellationToken = default);

	/// <summary>Deletes a stored image; a reference to no image stored here does nothing.</summary>
	/// <param name="image">The reference of the image to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(ContentReference image, CancellationToken cancellationToken = default);
}