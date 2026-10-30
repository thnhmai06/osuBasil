using Basil.Application.Content.Capabilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

namespace Basil.Infrastructure.Content.Capabilities;

/// <inheritdoc />
internal sealed class ImageSharpScreenshotInspector : IScreenshotInspector
{
	// Only the accepted formats are probed, so the header parsers of the other formats never see uploaded content.
	private static readonly DecoderOptions Options = new()
	{
		Configuration = new Configuration(new PngConfigurationModule(), new JpegConfigurationModule())
	};

	/// <inheritdoc />
	public async Task<ScreenshotInfo?> InspectAsync(Stream content, CancellationToken cancellationToken = default)
	{
		try
		{
			var info = await Image.IdentifyAsync(Options, content, cancellationToken);
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