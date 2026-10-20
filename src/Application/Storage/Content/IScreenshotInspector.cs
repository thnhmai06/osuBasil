namespace Basil.Application.Storage.Content;

/// <summary>Identifies the format and dimensions of uploaded picture content.</summary>
public interface IScreenshotInspector
{
	/// <summary>Reads a picture's format and dimensions without decoding it.</summary>
	/// <param name="content">The content stream, read from its current position and left open.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The picture's format and dimensions, or <see langword="null" /> when it is not PNG or JPEG.</returns>
	Task<ScreenshotInfo?> InspectAsync(Stream content, CancellationToken cancellationToken = default);
}

/// <summary>The supported format and dimensions of an uploaded picture.</summary>
/// <param name="Extension">The lower-case file extension, without a dot.</param>
/// <param name="Width">The picture width in pixels.</param>
/// <param name="Height">The picture height in pixels.</param>
public sealed record ScreenshotInfo(string Extension, int Width, int Height);