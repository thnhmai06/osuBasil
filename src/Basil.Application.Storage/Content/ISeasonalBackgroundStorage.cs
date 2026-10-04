namespace Basil.Application.Storage.Content;

/// <summary>Stores the seasonal main-menu backgrounds.</summary>
public interface ISeasonalBackgroundStorage
{
	/// <summary>Lists the names of the stored backgrounds.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The names of the stored backgrounds.</returns>
	Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Stores a background under a name, replacing any background with that name.</summary>
	/// <param name="name">The name to store the background under.</param>
	/// <param name="content">The image bytes.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task SaveAsync(string name, Stream content, CancellationToken cancellationToken = default);

	/// <summary>Opens a stored background.</summary>
	/// <param name="name">The name of the background to open.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The image bytes, or <see langword="null" /> when no background has that name.</returns>
	Task<Stream?> OpenAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>Renames a stored background.</summary>
	/// <param name="name">The current name of the background.</param>
	/// <param name="newName">The new name for the background.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task RenameAsync(string name, string newName, CancellationToken cancellationToken = default);

	/// <summary>Deletes a background; deleting a missing one does nothing.</summary>
	/// <param name="name">The name of the background to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}