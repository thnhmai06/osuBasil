using Basil.Domain.Content;

namespace Basil.Application.Storage.Contracts.Content;

/// <summary>Stores the image of the osu! main-menu icon.</summary>
public interface IMenuIconStorage
{
	/// <summary>Stores the icon image under a file name, replacing any stored icon.</summary>
	/// <param name="name">The file name to store the icon under.</param>
	/// <param name="content">The image bytes.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The reference to the stored icon.</returns>
	/// <exception cref="ArgumentException"><paramref name="name" /> is not a safe file name.</exception>
	Task<ContentReference> SaveAsync(string name, Stream content, CancellationToken cancellationToken = default);

	/// <summary>Opens the stored icon image.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The icon's reference and bytes, or <see langword="null" /> when no icon is stored.</returns>
	Task<(ContentReference Reference, Stream Content)?> OpenAsync(CancellationToken cancellationToken = default);

	/// <summary>Deletes the stored icon; deleting when none is stored does nothing.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(CancellationToken cancellationToken = default);
}