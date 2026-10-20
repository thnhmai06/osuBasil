using Basil.Application.Storage.Content;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

namespace Basil.Infrastructure.Services.Content;

internal sealed class ImageSharpScreenshotInspector : IScreenshotInspector
{
	public async Task<ScreenshotInfo?> InspectAsync(Stream content, CancellationToken cancellationToken = default)
	{
		try
		{
			var info = await Image.IdentifyAsync(content, cancellationToken);
			var extension = info.Metadata.DecodedImageFormat switch
			{
				PngFormat => "png",
				JpegFormat => "jpg",
				_ => null
			};
			return extension is null ? null : new ScreenshotInfo(extension, info.Width, info.Height);
		}
		catch (UnknownImageFormatException)
		{
			return null;
		}
		catch (InvalidImageContentException)
		{
			return null;
		}
	}
}
