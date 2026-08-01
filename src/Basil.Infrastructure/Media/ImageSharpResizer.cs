using Basil.Application.Abstractions.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Basil.Infrastructure.Media;

/// <summary>
///     Implements <see cref="IImageResizer" /> with SixLabors.ImageSharp.
/// </summary>
/// <remarks>
///     Crops to fill the target rectangle (<see cref="ResizeMode.Crop" />) rather than letterboxing,
///     matching how osu!'s own thumbnails behave, and encodes the result as JPEG.
/// </remarks>
public sealed class ImageSharpResizer : IImageResizer
{
	/// <inheritdoc cref="IImageResizer.ResizeAsync" />
	public async Task<byte[]> ResizeAsync(byte[] sourceImage, int width, int height,
		CancellationToken cancellationToken = default)
	{
		using var image = Image.Load(sourceImage);
		image.Mutate(x => x.Resize(new ResizeOptions
		{
			Size = new Size(width, height),
			Mode = ResizeMode.Crop
		}));

		using var output = new MemoryStream();
		await image.SaveAsync(output, new JpegEncoder(), cancellationToken);
		return output.ToArray();
	}
}