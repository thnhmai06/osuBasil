using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Content;

/// <summary>Stores the screenshots players upload and deletes them.</summary>
/// <remarks>A stored screenshot is read through the screenshot storage; it belongs to no user.</remarks>
public interface IScreenshotService
{
	/// <summary>The largest screenshot accepted, in bytes: 4 MiB.</summary>
	const int MaxScreenshotBytes = 4 * 1024 * 1024;

	/// <summary>Stores a screenshot under a new unique name.</summary>
	/// <param name="by">The connection uploading it; its user needs <see cref="Permissions.PlayerUploadScreenshots" />.</param>
	/// <param name="content">The image, read to its end; not disposed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The reference to the stored screenshot, or why it was not stored.</returns>
	/// <remarks>
	///     The image may be at most <see cref="MaxScreenshotBytes" /> bytes and must be a PNG or JPEG image; its format is
	///     told from its file, never from a name.
	/// </remarks>
	Task<(ContentReference? Screenshot, ScreenshotUploadFailure? Failure)> UploadAsync(Connection by, Stream content,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a stored screenshot.</summary>
	/// <param name="by">The connection deleting it; its user needs <see cref="Permissions.ModeratorDeleteScreenshots" />.</param>
	/// <param name="screenshot">The reference of the screenshot to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> if the caller may delete screenshots, even when no screenshot is stored under the
	///     reference; otherwise, <see langword="false" />.
	/// </returns>
	Task<bool> DeleteAsync(Connection by, ContentReference screenshot, CancellationToken cancellationToken = default);
}

/// <summary>The reasons a screenshot is not stored.</summary>
public enum ScreenshotUploadFailure : byte
{
	/// <summary>The caller may not upload screenshots, or the permission is suspended.</summary>
	NotAuthorized,

	/// <summary>The image is larger than <see cref="IScreenshotService.MaxScreenshotBytes" />.</summary>
	TooLarge,

	/// <summary>The file is not a PNG or JPEG image.</summary>
	InvalidImage
}