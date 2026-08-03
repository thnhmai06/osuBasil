using System.Diagnostics;
using Basil.Infrastructure.Media;

namespace Basil.Infrastructure.Tests.Media;

/// <summary>
///     Exercises the real ffmpeg binary (not a mock) — soft-skips if ffmpeg isn't reachable on PATH,
///     since that's an environment dependency this test suite doesn't otherwise require (see
///     docs/run-deployment.md's Docker-vs-manual-publish split for where ffmpeg is expected to come
///     from in a real deployment).
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