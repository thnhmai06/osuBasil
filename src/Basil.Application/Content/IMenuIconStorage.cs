namespace Basil.Application.Content;

/// <summary>Stores the image of the osu! main-menu icon.</summary>
public interface IMenuIconStorage
{
	/// <summary>Stores the icon image under a file name, replacing any stored icon.</summary>
	/// <param name="name">The file name to store the icon under.</param>
	/// <param name="content">The image bytes.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task SaveAsync(string name, Stream content, CancellationToken cancellationToken = default);

	/// <summary>Opens the stored icon image.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The icon's file name and bytes, or <see langword="null" /> when no icon is stored.</returns>
	Task<(string Name, Stream Content)?> OpenAsync(CancellationToken cancellationToken = default);

	/// <summary>Deletes the stored icon; deleting when none is stored does nothing.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(CancellationToken cancellationToken = default);
}