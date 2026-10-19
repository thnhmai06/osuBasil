using Basil.Application.Services.Contracts.Content;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Implementations.Users;
using Basil.Application.Storage.Contracts.Content;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Content;

/// <summary>Stores the screenshots players upload and deletes them.</summary>
internal sealed class ScreenshotService(IScreenshotStorage screenshots, TimeProvider time) : IScreenshotService
{
	/// <inheritdoc />
	public async Task<(ContentReference? Screenshot, ScreenshotUploadFailure? Failure)> UploadAsync(Connection by,
		Stream content, CancellationToken cancellationToken = default)
	{
		if (!PermissionRules.Allows(by, Permissions.ScreenshotUpload, time.GetUtcNow()))
			return (null, ScreenshotUploadFailure.NotAuthorized);

		using var memory = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var allowance = (int)Math.Min(buffer.Length, IScreenshotService.MaxScreenshotBytes + 1 - memory.Length);
			if (allowance == 0) break;

			var read = await content.ReadAsync(buffer.AsMemory(0, allowance), cancellationToken).ConfigureAwait(false);
			if (read == 0) break;
			memory.Write(buffer, 0, read);
		}

		if (memory.Length > IScreenshotService.MaxScreenshotBytes)
			return (null, ScreenshotUploadFailure.TooLarge);

		var extension = ImageExtension(memory.GetBuffer().AsSpan(0, (int)memory.Length));
		if (extension is null) return (null, ScreenshotUploadFailure.InvalidImage);

		memory.Position = 0;
		var reference = await screenshots.SaveAsync(memory, extension, cancellationToken).ConfigureAwait(false);
		return (reference, null);
	}

	/// <inheritdoc />
	public async Task<bool> DeleteAsync(Connection by, ContentReference screenshot,
		CancellationToken cancellationToken = default)
	{
		if (!PermissionRules.Allows(by, Permissions.ScreenshotDelete, time.GetUtcNow())) return false;
		await screenshots.DeleteAsync(screenshot, cancellationToken).ConfigureAwait(false);
		return true;
	}

	/// <summary>Tells which image format the file is.</summary>
	/// <param name="content">The whole image.</param>
	/// <returns>
	///     The file extension of the format, or <see langword="null" /> when the file is neither a JPEG nor a
	///     PNG image.
	/// </returns>
	private static string? ImageExtension(ReadOnlySpan<byte> content)
	{
		if (content.Length >= 5 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF &&
		    content[^2] == 0xFF && content[^1] == 0xD9)
			return "jpg";

		if (content.Length >= 16 &&
		    content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47 &&
		    content[4] == 0x0D && content[5] == 0x0A && content[6] == 0x1A && content[7] == 0x0A &&
		    content[^8] == 0x49 && content[^7] == 0x45 && content[^6] == 0x4E && content[^5] == 0x44 &&
		    content[^4] == 0xAE && content[^3] == 0x42 && content[^2] == 0x60 && content[^1] == 0x82)
			return "png";

		return null;
	}
}
