using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Content;

/// <summary>Stores the screenshots players upload and deletes them.</summary>
/// <remarks>A stored screenshot is read through the screenshot storage; it belongs to no user.</remarks>
public interface IScreenshotService
{
	/// <summary>Stores a screenshot under a new unique name.</summary>
	/// <param name="by">The connection uploading it; its user needs <see cref="Permissions.PlayerUploadScreenshots" />.</param>
	/// <param name="content">The image, read to its end; not disposed.</param>
	/// <param name="extension">The file extension of the image format, without the dot.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The reference to the stored screenshot, or <see langword="null" /> when the caller may not upload screenshots.</returns>
	/// <exception cref="ArgumentException"><paramref name="extension" /> is not 1 to 8 ASCII letters or digits.</exception>
	Task<AssetReference?> UploadAsync(Connection by, Stream content, string extension,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a stored screenshot.</summary>
	/// <param name="by">The connection deleting it; its user needs <see cref="Permissions.ModeratorDeleteScreenshots" />.</param>
	/// <param name="screenshot">The reference of the screenshot to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> if the caller may delete screenshots, even when no screenshot is stored under the
	///     reference; otherwise, <see langword="false" />.
	/// </returns>
	Task<bool> DeleteAsync(Connection by, AssetReference screenshot, CancellationToken cancellationToken = default);
}
