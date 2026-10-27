using Basil.Domain.Content;

namespace Basil.Application.Content.Persistence;

/// <summary>Stores the seasonal main-menu backgrounds.</summary>
public interface IMenuSeasonalsStorage
{
	/// <summary>Lists the stored backgrounds.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>References to the stored backgrounds.</returns>
	Task<IEnumerable<ContentReference>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Stores a background under a name, replacing any background with that name.</summary>
	/// <param name="name">The name to store the background under.</param>
	/// <param name="content">The image bytes.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The reference to the stored background.</returns>
	/// <exception cref="ArgumentException"><paramref name="name" /> is not a safe file name.</exception>
	Task<ContentReference> SaveAsync(string name, Stream content, CancellationToken cancellationToken = default);

	/// <summary>Opens a stored background.</summary>
	/// <param name="background">The reference of the background to open.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The image bytes, or <see langword="null" /> when the reference names no background stored here.</returns>
	Task<Stream?> OpenAsync(ContentReference background, CancellationToken cancellationToken = default);

	/// <summary>Renames a stored background.</summary>
	/// <param name="background">The reference of the background to rename.</param>
	/// <param name="newName">The new name for the background.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The reference under the new name, or <see langword="null" /> when the reference names no background stored
	///     here or the new name is taken.
	/// </returns>
	/// <exception cref="ArgumentException"><paramref name="newName" /> is not a safe file name.</exception>
	Task<ContentReference?> RenameAsync(ContentReference background, string newName,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a stored background; a reference to no background stored here does nothing.</summary>
	/// <param name="background">The reference of the background to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(ContentReference background, CancellationToken cancellationToken = default);
}