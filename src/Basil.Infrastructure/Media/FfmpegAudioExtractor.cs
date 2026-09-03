using System.Globalization;
using Basil.Application.Abstractions.Media;
using FFMpegCore;
using FFMpegCore.Enums;

namespace Basil.Infrastructure.Media;

/// <summary>
///     Implements <see cref="IAudioExtractor" /> by trimming an audio file with an external ffmpeg
///     executable.
/// </summary>
/// <remarks>
///     Trims the clip by seeking to the requested start offset (clamped to 0), disables the video
///     channel, and encodes the requested duration as a 128kbps MP3. The last second of the clip
///     fades out, so playback never cuts off abruptly. Requires a ffmpeg executable on PATH.
/// </remarks>
public sealed class FfmpegAudioExtractor : IAudioExtractor
{
	/// <summary>The length of the fade-out applied to the end of every extracted clip.</summary>
	private static readonly TimeSpan FadeOutDuration = TimeSpan.FromSeconds(1);

	/// <inheritdoc cref="IAudioExtractor.ExtractAsync" />
	public async Task<byte[]> ExtractAsync(string audioFilePath, int startMs, TimeSpan duration,
		CancellationToken cancellationToken = default)
	{
		var tempOutput = Path.Combine(Path.GetTempPath(), $"basil-preview-{Guid.NewGuid()}.mp3");
		try
		{
			var fadeStartSeconds = Math.Max((duration - FadeOutDuration).TotalSeconds, 0)
				.ToString(CultureInfo.InvariantCulture);
			await FFMpegArguments
				.FromFileInput(audioFilePath, true,
					options => options.Seek(TimeSpan.FromMilliseconds(Math.Max(startMs, 0))))
				.OutputToFile(tempOutput, true, options => options
					.WithDuration(duration)
					.DisableChannel(Channel.Video)
					.WithAudioCodec(AudioCodec.LibMp3Lame)
					.WithAudioBitrate(128)
					.WithCustomArgument(
						$"-af afade=t=out:st={fadeStartSeconds}:d={FadeOutDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)}"))
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