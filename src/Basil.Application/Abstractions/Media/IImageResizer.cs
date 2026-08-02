namespace Basil.Application.Abstractions.Media;

/// <summary>
///     Resizes an already-decoded image to a fixed pixel size.
/// </summary>
/// <remarks>
///     Used to produce the resized thumbnails served by the <c>b.&lt;domain&gt;</c> host for a
///     beatmapset's background image. The resize crops to fill the target rectangle rather than
///     letterboxing, which matches how osu!'s own thumbnails behave.
/// </remarks>
public interface IImageResizer
{
	/// <summary>
	///     Resizes the given image to the target dimensions and returns it as JPEG bytes.
	/// </summary>
	/// <param name="sourceImage">The raw bytes of the source image.</param>
	/// <param name="width">The target width in pixels.</param>
	/// <param name="height">The target height in pixels.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The resized image encoded as JPEG bytes.</returns>
	Task<byte[]> ResizeAsync(byte[] sourceImage, int width, int height,
		CancellationToken cancellationToken = default);
}