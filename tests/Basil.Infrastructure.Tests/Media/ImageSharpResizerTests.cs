using Basil.Infrastructure.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Basil.Infrastructure.Tests.Media;

public class ImageSharpResizerTests
{
	[Fact]
	public async Task ResizeAsync_ProducesJpegAtExactRequestedSize()
	{
		using var source = new Image<Rgba32>(400, 300);
		using var sourceStream = new MemoryStream();
		await source.SaveAsync(sourceStream, new JpegEncoder());
		var sourceBytes = sourceStream.ToArray();

		var resizer = new ImageSharpResizer();
		var result = await resizer.ResizeAsync(sourceBytes, 80, 60);

		using var resized = Image.Load(result);
		Assert.Equal(80, resized.Width);
		Assert.Equal(60, resized.Height);
	}
}