namespace Basil.Application.Storage.Content;

/// <summary>Stores the images of main-menu banners.</summary>
public interface IMenuBannerStorage
{
	/// <summary>Lists the names of the stored images.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The names of the stored images.</returns>
	Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Stores an image under a name, replacing any image with that name.</summary>
	/// <param name="name">The name to store the image under.</param>
	/// <param name="content">The image bytes.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task SaveAsync(string name, Stream content, CancellationToken cancellationToken = default);

	/// <summary>Opens a stored image.</summary>
	/// <param name="name">The name of the image to open.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The image bytes, or <see langword="null" /> when no image has that name.</returns>
	Task<Stream?> OpenAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>Deletes an image; deleting a missing one does nothing.</summary>
	/// <param name="name">The name of the image to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}