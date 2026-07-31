namespace Basil.Application.Abstractions.Media;

/// <summary>
///     Resizes an already-decoded image (a mapset's background) to a fixed pixel size, cropping to
///     fill rather than letterboxing — matches how osu!'s own thumbnails behave. Implemented against
///     SixLabors.ImageSharp in Basil.Infrastructure (kept behind this port so Basil.Web never
///     references ImageSharp directly, per the architecture dependency rule).
/// </summary>
public interface IImageResizer
{
	Task<byte[]> ResizeAsync(byte[] sourceImage, int width, int height,
		CancellationToken cancellationToken = default);
}
