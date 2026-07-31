using Basil.Application.Abstractions.Media;
using FFMpegCore;
using FFMpegCore.Enums;

namespace Basil.Infrastructure.Media;

/// <inheritdoc cref="IAudioPreviewExtractor" />
public sealed class FFMpegAudioPreviewExtractor : IAudioPreviewExtractor
{
	public async Task<byte[]> ExtractAsync(string audioFilePath, int startMs, TimeSpan duration,
		CancellationToken cancellationToken = default)
	{
		var tempOutput = Path.Combine(Path.GetTempPath(), $"basil-preview-{Guid.NewGuid()}.mp3");
		try
		{
			await FFMpegArguments
				.FromFileInput(audioFilePath, true,
					options => options.Seek(TimeSpan.FromMilliseconds(Math.Max(startMs, 0))))
				.OutputToFile(tempOutput, true, options => options
					.WithDuration(duration)
					.DisableChannel(Channel.Video)
					.WithAudioCodec(AudioCodec.LibMp3Lame)
					.WithAudioBitrate(128))
				.CancellableThrough(cancellationToken)
				.ProcessAsynchronously();

			return await File.ReadAllBytesAsync(tempOutput, cancellationToken);
		}
		finally
		{
			if (File.Exists(tempOutput)) File.Delete(tempOutput);
		}
	}
}
