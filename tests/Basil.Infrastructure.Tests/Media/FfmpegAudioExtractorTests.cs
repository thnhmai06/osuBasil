using System.Diagnostics;
using System.Globalization;
using Basil.Infrastructure.Media;

namespace Basil.Infrastructure.Tests.Media;

/// <summary>
///     Exercises the real ffmpeg binary (not a mock) — soft-skips if ffmpeg isn't reachable on PATH,
///     since that's an environment dependency this test suite doesn't otherwise require (see
///     docs/for-technicians/deployment.md for where ffmpeg is expected to come from in a real
///     deployment).
/// </summary>
public class FfmpegAudioExtractorTests
{
	[Fact]
	public async Task ExtractAsync_RealFfmpeg_ProducesNonEmptyMp3Clip()
	{
		if (!await IsFfmpegAvailableAsync()) return;

		var sourcePath = Path.Combine(Path.GetTempPath(), $"basil-preview-test-{Guid.NewGuid()}.wav");
		try
		{
			await GenerateSineWaveAsync(sourcePath, TimeSpan.FromSeconds(3));

			var extractor = new FfmpegAudioExtractor();
			var clip = await extractor.ExtractAsync(sourcePath, 0, TimeSpan.FromSeconds(2));

			Assert.True(clip.Length > 1000, "expected a non-trivial mp3 clip");
		}
		finally
		{
			if (File.Exists(sourcePath)) File.Delete(sourcePath);
		}
	}

	/// <summary>
	///     Regression test (Issue #4): "Add a 1-second fade-out to audio previews before they end."
	/// </summary>
	[Fact]
	public async Task ExtractAsync_RealFfmpeg_FadesOutBeforeClipEnds()
	{
		if (!await IsFfmpegAvailableAsync()) return;

		var sourcePath = Path.Combine(Path.GetTempPath(), $"basil-preview-fade-test-{Guid.NewGuid()}.wav");
		try
		{
			await GenerateSineWaveAsync(sourcePath, TimeSpan.FromSeconds(3));

			var extractor = new FfmpegAudioExtractor();
			var clip = await extractor.ExtractAsync(sourcePath, 0, TimeSpan.FromSeconds(3));

			var midAmplitude =
				await AverageAbsAmplitudeAsync(clip, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100));
			var endAmplitude =
				await AverageAbsAmplitudeAsync(clip, TimeSpan.FromMilliseconds(2900), TimeSpan.FromMilliseconds(100));

			Assert.True(endAmplitude < midAmplitude * 0.5,
				$"expected the clip's last 100ms to be quieter than its middle (mid={midAmplitude}, end={endAmplitude})");
		}
		finally
		{
			if (File.Exists(sourcePath)) File.Delete(sourcePath);
		}
	}

	/// <summary>Decodes a window of the given mp3 clip to raw PCM and returns its average absolute sample value.</summary>
	private static async Task<double> AverageAbsAmplitudeAsync(byte[] mp3Bytes, TimeSpan start, TimeSpan duration)
	{
		var mp3Path = Path.Combine(Path.GetTempPath(), $"basil-fade-check-{Guid.NewGuid()}.mp3");
		var pcmPath = Path.Combine(Path.GetTempPath(), $"basil-fade-check-{Guid.NewGuid()}.pcm");
		try
		{
			await File.WriteAllBytesAsync(mp3Path, mp3Bytes);
			using var process = Process.Start(new ProcessStartInfo("ffmpeg",
				$"-ss {start.TotalSeconds.ToString(CultureInfo.InvariantCulture)} " +
				$"-t {duration.TotalSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{mp3Path}\" " +
				$"-f s16le -ac 1 -ar 44100 -y \"{pcmPath}\"")
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true
			})!;
			await process.WaitForExitAsync();

			var samples = await File.ReadAllBytesAsync(pcmPath);
			var sampleCount = samples.Length / 2;
			if (sampleCount == 0) return 0;

			long sum = 0;
			for (var i = 0; i < sampleCount; i++)
				sum += Math.Abs((int)BitConverter.ToInt16(samples, i * 2));
			return (double)sum / sampleCount;
		}
		finally
		{
			if (File.Exists(mp3Path)) File.Delete(mp3Path);
			if (File.Exists(pcmPath)) File.Delete(pcmPath);
		}
	}

	private static async Task<bool> IsFfmpegAvailableAsync()
	{
		try
		{
			using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
				{ RedirectStandardOutput = true, RedirectStandardError = true });
			if (process is null) return false;
			await process.WaitForExitAsync();
			return process.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	private static async Task GenerateSineWaveAsync(string path, TimeSpan duration)
	{
		using var process = Process.Start(new ProcessStartInfo("ffmpeg",
			$"-f lavfi -i sine=frequency=440:duration={duration.TotalSeconds} -y \"{path}\"")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true
		})!;
		await process.WaitForExitAsync();
	}
}