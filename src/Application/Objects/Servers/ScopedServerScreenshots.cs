using Basil.Application.Objects.Sessions;
using Basil.Domain.Content;
using Basil.Domain.Permissions;

namespace Basil.Application.Objects.Servers;

/// <summary>Screenshot operations scoped to one connection.</summary>
public sealed class ScopedServerScreenshots
{
	/// <summary>The largest screenshot accepted, in bytes: 4 MiB.</summary>
	public const int MaxScreenshotBytes = 4 * 1024 * 1024;

	private readonly ScopedServer _scoped;

	/// <summary>Creates screenshot operations for a server scope.</summary>
	/// <param name="scoped">The server scope.</param>
	internal ScopedServerScreenshots(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Stores a screenshot under a new unique name.</summary>
	/// <param name="content">The image, read to its end; not disposed.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The reference to the stored screenshot, or why it was not stored.</returns>
	/// <remarks>
	///     Requires <see cref="Permissions.ScreenshotUpload" />. The image may be at most
	///     <see cref="MaxScreenshotBytes" /> bytes and must be a PNG or JPEG image; its format is told from its file,
	///     never from a name.
	/// </remarks>
	public async Task<(ContentReference? Screenshot, ScreenshotUploadFailure? Failure)> UploadAsync(Stream content,
		CancellationToken ct = default)
	{
		var context = _scoped.Server.Context;
		if (_scoped.By.Session.Check(Permissions.ScreenshotUpload, context.Session.Clock.GetUtcNow()) is not Access
			    .Allowed)
			return (null, ScreenshotUploadFailure.NotAuthorized);

		using var memory = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var allowance = (int)Math.Min(buffer.Length, MaxScreenshotBytes + 1 - memory.Length);
			if (allowance == 0) break;
			var read = await content.ReadAsync(buffer.AsMemory(0, allowance), ct);
			if (read == 0) break;
			memory.Write(buffer, 0, read);
		}

		if (memory.Length > MaxScreenshotBytes) return (null, ScreenshotUploadFailure.TooLarge);

		var extension = ImageExtension(memory.GetBuffer().AsSpan(0, (int)memory.Length));
		if (extension is null) return (null, ScreenshotUploadFailure.InvalidImage);

		memory.Position = 0;
		var reference = await context.Screenshots.SaveAsync(memory, extension, ct);
		return (reference, null);
	}

	/// <summary>Deletes a stored screenshot.</summary>
	/// <param name="screenshot">The reference of the screenshot to delete.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> if the caller may delete screenshots, even when no screenshot is stored under the
	///     reference; otherwise, <see langword="false" />.
	/// </returns>
	/// <remarks>Requires <see cref="Permissions.ScreenshotDelete" />.</remarks>
	public async Task<bool> DeleteAsync(ContentReference screenshot, CancellationToken ct = default)
	{
		var context = _scoped.Server.Context;
		if (_scoped.By.Session.Check(Permissions.ScreenshotDelete, context.Session.Clock.GetUtcNow()) is not Access
			    .Allowed)
			return false;
		await context.Screenshots.DeleteAsync(screenshot, ct);
		return true;
	}

	private static string? ImageExtension(ReadOnlySpan<byte> content)
	{
		return content.Length switch
		{
			>= 5 when content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF && content[^2] == 0xFF &&
			          content[^1] == 0xD9 => "jpg",
			>= 16 when content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47 &&
			           content[4] == 0x0D && content[5] == 0x0A && content[6] == 0x1A && content[7] == 0x0A &&
			           content[^8] == 0x49 && content[^7] == 0x45 && content[^6] == 0x4E && content[^5] == 0x44 &&
			           content[^4] == 0xAE && content[^3] == 0x42 && content[^2] == 0x60 &&
			           content[^1] == 0x82 => "png",
			_ => null
		};
	}
}

/// <summary>The reasons a screenshot is not stored.</summary>
public enum ScreenshotUploadFailure : byte
{
	/// <summary>The caller may not upload screenshots, or the permission is suspended.</summary>
	NotAuthorized,

	/// <summary>The image is larger than <see cref="ScopedServerScreenshots.MaxScreenshotBytes" />.</summary>
	TooLarge,

	/// <summary>The file is not a PNG or JPEG image.</summary>
	InvalidImage
}