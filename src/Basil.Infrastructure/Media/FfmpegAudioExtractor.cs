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
///     channel, and encodes the requested duration as a 128kbps MP3. Requires a ffmpeg executable
///     on PATH.
/// </remarks>
public sealed class FfmpegAudioExtractor : IAudioExtractor
{
	/// <inheritdoc cref="IAudioExtractor.ExtractAsync" />
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