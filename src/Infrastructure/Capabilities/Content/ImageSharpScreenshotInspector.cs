using Basil.Application.Storage.Content;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

namespace Basil.Infrastructure.Capabilities.Content;

/// <inheritdoc />
internal sealed class ImageSharpScreenshotInspector : IScreenshotInspector
{
	/// <inheritdoc />
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
			return extension is not null ? new ScreenshotInfo(extension, info.Width, info.Height) : null;
		}
		catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException)
		{
			return null;
		}
	}
}