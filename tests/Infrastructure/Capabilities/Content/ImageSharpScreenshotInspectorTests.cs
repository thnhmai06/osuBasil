using Basil.Application.Capabilities.Content;
using Basil.Infrastructure.Capabilities.Content;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Basil.Infrastructure.Capabilities.Tests.Content;

/// <summary>Pins which pictures the inspector accepts, and that it reads dimensions without decoding them.</summary>
public sealed class ImageSharpScreenshotInspectorTests
{
	private readonly ImageSharpScreenshotInspector _inspector = new();

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>A PNG is reported as a PNG with its own dimensions.</summary>
	[Fact]
	public async Task InspectAsync_ReportsAPngWithItsDimensions()
	{
		await using var content = Picture(1920, 1080, new PngEncoder());

		var info = await _inspector.InspectAsync(content, Token);

		Assert.Equal(new ScreenshotInfo("png", 1920, 1080), info);
	}

	/// <summary>A JPEG is reported as a JPEG with its own dimensions.</summary>
	[Fact]
	public async Task InspectAsync_ReportsAJpegWithItsDimensions()
	{
		await using var content = Picture(1280, 720, new JpegEncoder());

		var info = await _inspector.InspectAsync(content, Token);

		Assert.Equal(new ScreenshotInfo("jpg", 1280, 720), info);
	}

	/// <summary>A GIF is not a picture the server accepts.</summary>
	[Fact]
	public async Task InspectAsync_ReturnsNullForAGif()
	{
		await using var content = Picture(64, 64, new GifEncoder());

		var info = await _inspector.InspectAsync(content, Token);

		Assert.Null(info);
	}

	/// <summary>A bitmap is not a picture the server accepts.</summary>
	[Fact]
	public async Task InspectAsync_ReturnsNullForABitmap()
	{
		await using var content = Picture(64, 64, new BmpEncoder());

		var info = await _inspector.InspectAsync(content, Token);

		Assert.Null(info);
	}

	/// <summary>Content that is no picture at all is not a screenshot.</summary>
	/// <param name="length">How many bytes of content there are.</param>
	[Theory]
	[InlineData(0)]
	[InlineData(8)]
	[InlineData(64)]
	public async Task InspectAsync_ReturnsNullForContentThatIsNoPicture(int length)
	{
		await using var content = new MemoryStream(new byte[length]);

		var info = await _inspector.InspectAsync(content, Token);

		Assert.Null(info);
	}

	/// <summary>A picture cut off before its header is not a screenshot.</summary>
	[Fact]
	public async Task InspectAsync_ReturnsNullForAPictureCutOffBeforeItsHeader()
	{
		await using var full = Picture(64, 64, new PngEncoder());
		await using var cut = new MemoryStream(full.ToArray().AsSpan(0, 4).ToArray());

		var info = await _inspector.InspectAsync(cut, Token);

		Assert.Null(info);
	}

	/// <summary>Dimensions come from the header, so a picture cut off after it still has its dimensions.</summary>
	[Fact]
	public async Task InspectAsync_ReadsTheDimensionsWithoutDecodingThePicture()
	{
		await using var full = Picture(640, 480, new PngEncoder());
		// The PNG signature and the IHDR chunk end at byte 33; nothing of the picture itself follows.
		await using var header = new MemoryStream(full.ToArray().AsSpan(0, 33).ToArray());

		var info = await _inspector.InspectAsync(header, Token);

		Assert.Equal(new ScreenshotInfo("png", 640, 480), info);
	}

	/// <summary>Builds a picture of the given size, encoded in the given format.</summary>
	/// <param name="width">The picture width in pixels.</param>
	/// <param name="height">The picture height in pixels.</param>
	/// <param name="encoder">The encoder of the picture format.</param>
	/// <returns>The encoded picture, read from its start.</returns>
	private static MemoryStream Picture(int width, int height, IImageEncoder encoder)
	{
		using var image = new Image<Rgba32>(width, height);
		var content = new MemoryStream();
		image.Save(content, encoder);
		content.Position = 0;
		return content;
	}
}
