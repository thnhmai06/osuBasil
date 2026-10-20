using Basil.Application.Objects.Sessions;
using Basil.Domain.Content;
using Basil.Domain.Permissions;

namespace Basil.Application.Objects.Servers;

/// <summary>Screenshot operations scoped to one connection.</summary>
public sealed class ScopedServerScreenshots
{
	/// <summary>The largest screenshot accepted, in bytes: 4 MiB.</summary>
	public const int MaxScreenshotBytes = 4 * 1024 * 1024;

	/// <summary>The largest accepted screenshot width, in pixels.</summary>
	public const int MaxScreenshotWidth = 8192;

	/// <summary>The largest accepted screenshot height, in pixels.</summary>
	public const int MaxScreenshotHeight = 8192;

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
	///     <see cref="MaxScreenshotBytes" /> bytes and must be a PNG or JPEG picture of at most
	///     <see cref="MaxScreenshotWidth" /> by <see cref="MaxScreenshotHeight" /> pixels per side, told from its
	///     content, never from a name.
	/// </remarks>
	public async Task<(ContentReference? Screenshot, ScreenshotUploadFailure? Failure)> UploadAsync(Stream content,
		CancellationToken ct = default)
	{
		var context = _scoped.Server.Context;
		if (_scoped.By.User.Check(Permissions.ScreenshotUpload, context.Session.Clock.GetUtcNow()) is not Access
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

		memory.Position = 0;
		var info = await context.Inspector.InspectAsync(memory, ct);
		if (info is null || info.Width < 1 || info.Height < 1)
			return (null, ScreenshotUploadFailure.InvalidImage);
		if (info.Width > MaxScreenshotWidth || info.Height > MaxScreenshotHeight)
			return (null, ScreenshotUploadFailure.DimensionsTooLarge);

		memory.Position = 0;
		var reference = await context.Screenshots.SaveAsync(memory, info.Extension, ct);
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
		if (_scoped.By.User.Check(Permissions.ScreenshotDelete, context.Session.Clock.GetUtcNow()) is not Access
			    .Allowed)
			return false;
		await context.Screenshots.DeleteAsync(screenshot, ct);
		return true;
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
	InvalidImage,

	/// <summary>The picture is wider or taller than the accepted size.</summary>
	DimensionsTooLarge
}