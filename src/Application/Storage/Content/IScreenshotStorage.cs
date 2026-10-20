using Basil.Domain.Content;

namespace Basil.Application.Storage.Content;

/// <summary>Stores the screenshots players upload.</summary>
public interface IScreenshotStorage
{
	/// <summary>Lists the stored screenshots.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>References to the stored screenshots.</returns>
	Task<IEnumerable<ContentReference>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Stores a screenshot under a new unique name.</summary>
	/// <param name="content">The image bytes.</param>
	/// <param name="extension">The file extension of the image format, without the dot.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The reference to the stored screenshot.</returns>
	/// <exception cref="ArgumentException"><paramref name="extension" /> is not 1 to 8 ASCII letters or digits.</exception>
	Task<ContentReference> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);

	/// <summary>Opens a stored screenshot.</summary>
	/// <param name="screenshot">The reference of the screenshot to open.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The image bytes, or <see langword="null" /> when the reference names no screenshot stored here.</returns>
	Task<Stream?> OpenAsync(ContentReference screenshot, CancellationToken cancellationToken = default);

	/// <summary>Deletes a stored screenshot; a reference to no screenshot stored here does nothing.</summary>
	/// <param name="screenshot">The reference of the screenshot to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(ContentReference screenshot, CancellationToken cancellationToken = default);
}