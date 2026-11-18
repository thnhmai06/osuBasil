using Basil.Application.Content.Ports;
using Basil.Infrastructure.Content.Adapters;
using Basil.Testing.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.PixelFormats;

namespace Basil.Infrastructure.Content.Tests.Adapters;

public sealed class ImageSharpScreenshotInspectorTests
{
	private readonly ImageSharpScreenshotInspector _inspector = new();

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task InspectAsync_ReportsAPngWithItsDimensions()
	{
		await using var content = Picture(1920, 1080, new PngEncoder());

		var info = await _inspector.InspectAsync(content, Token);

		Assert.Equal(new ScreenshotInfo("png", 1920, 1080), info);
	}

	[Fact]
	public async Task InspectAsync_ReportsAJpegWithItsDimensions()
	{
		await using var content = Picture(1280, 720, new JpegEncoder());

		var info = await _inspector.InspectAsync(content, Token);

		Assert.Equal(new ScreenshotInfo("jpg", 1280, 720), info);
	}

	[Fact]
	public async Task InspectAsync_ReturnsNullForAGif()
	{
		await using var content = Picture(64, 64, new GifEncoder());

		var info = await _inspector.InspectAsync(content, Token);

		Assert.Null(info);
	}

	[Fact]
	public async Task InspectAsync_ReturnsNullForATiff()
	{
		await using var content = Picture(64, 64, new TiffEncoder());

		var info = await _inspector.InspectAsync(content, Token);

		Assert.Null(info);
	}

	[Fact]
	public async Task InspectAsync_ReturnsNullForABitmap()
	{
		await using var content = Picture(64, 64, new BmpEncoder());

		var info = await _inspector.InspectAsync(content, Token);

		Assert.Null(info);
	}

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

	[Fact]
	public async Task InspectAsync_ReturnsNullForAPictureCutOffBeforeItsHeader()
	{
		await using var full = Picture(64, 64, new PngEncoder());
		await using var cut = new MemoryStream(full.ToArray().AsSpan(0, 4).ToArray());

		var info = await _inspector.InspectAsync(cut, Token);

		Assert.Null(info);
	}

	[Fact]
	public async Task InspectAsync_ReadsTheDimensionsWithoutDecodingThePicture()
	{
		await using var full = Picture(640, 480, new PngEncoder());
		// The PNG signature and the IHDR chunk end at byte 33; nothing of the picture itself follows.
		await using var header = new MemoryStream(full.ToArray().AsSpan(0, 33).ToArray());

		var info = await _inspector.InspectAsync(header, Token);

		Assert.Equal(new ScreenshotInfo("png", 640, 480), info);
	}

	private static MemoryStream Picture(int width, int height, IImageEncoder encoder)
	{
		using var image = new Image<Rgba32>(width, height);
		var content = new MemoryStream();
		image.Save(content, encoder);
		content.Position = 0;
		return content;
	}
}

public sealed class ImageSharpScreenshotInspectorRealPictureTests
{
	private readonly ImageSharpScreenshotInspector _inspector = new();

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static MemoryStream Open(string name)
	{
		return new MemoryStream(OszArchive.Sample(Path.Combine("Images", name)), false);
	}

	[Fact]
	public async Task InspectAsync_ReportsARealAvatarPng()
	{
		await using var content = Open("avatar-default.png");

		Assert.Equal(new ScreenshotInfo("png", 512, 512), await _inspector.InspectAsync(content, Token));
	}

	[Fact]
	public async Task InspectAsync_ReportsARealJpegWithExifData()
	{
		await using var content = Open("background-1920x1200.jpg");

		Assert.Equal(new ScreenshotInfo("jpg", 1920, 1200), await _inspector.InspectAsync(content, Token));
	}

	[Fact]
	public async Task InspectAsync_ReportsTheDimensionsOfAnInterlacedPngFromItsHeader()
	{
		await using var content = Open("seasonal-3500x2487-header.png");

		Assert.Equal(new ScreenshotInfo("png", 3500, 2487), await _inspector.InspectAsync(content, Token));
	}
}