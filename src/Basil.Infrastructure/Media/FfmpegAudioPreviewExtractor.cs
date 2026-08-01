using Basil.Application.Abstractions.Media;
using FFMpegCore;
using FFMpegCore.Enums;

namespace Basil.Infrastructure.Media;

/// <summary>
///     Implements <see cref="IAudioPreviewExtractor" /> by shelling out to the ffmpeg binary through
///     FFMpegCore.
/// </summary>
/// <remarks>
///     Trims the clip by seeking to the requested start offset (clamped to 0), disables the video
///     channel, and encodes the requested duration with the libmp3lame codec at 128kbps. The output
///     goes to a uniquely named temp file which is read back as bytes and deleted in a
///     <c>finally</c> block. Requires a ffmpeg executable on PATH.
/// </remarks>
public sealed class FfmpegAudioPreviewExtractor : IAudioPreviewExtractor
{
	/// <inheritdoc cref="IAudioPreviewExtractor.ExtractAsync" />
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