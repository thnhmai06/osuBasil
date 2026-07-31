using Basil.Application.Abstractions.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Basil.Infrastructure.Media;

/// <inheritdoc cref="IImageResizer" />
public sealed class ImageSharpResizer : IImageResizer
{
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
