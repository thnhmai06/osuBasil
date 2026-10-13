using Basil.Application.Services.Contracts.Content;
using Basil.Application.Services.Implementations.Users;
using Basil.Application.Storage.Contracts.Content;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Content;

/// <summary>Stores the screenshots players upload and deletes them.</summary>
internal sealed class ScreenshotService(IScreenshotStorage screenshots, TimeProvider time) : IScreenshotService
{
	/// <inheritdoc />
	public async Task<AssetReference?> UploadAsync(Connection by, Stream content, string extension,
		CancellationToken cancellationToken = default)
	{
		if (!PermissionRules.Allows(by, Permissions.PlayerUploadScreenshots, time.GetUtcNow())) return null;
		return await screenshots.SaveAsync(content, extension, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<bool> DeleteAsync(Connection by, AssetReference screenshot,
		CancellationToken cancellationToken = default)
	{
		if (!PermissionRules.Allows(by, Permissions.ModeratorDeleteScreenshots, time.GetUtcNow())) return false;
		await screenshots.DeleteAsync(screenshot, cancellationToken).ConfigureAwait(false);
		return true;
	}
}
