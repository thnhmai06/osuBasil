using Basil.Application.Beatmaps.Ports;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.SoundFile;
using NAudio.Wave;

namespace Basil.Infrastructure.Beatmaps.Adapters;

internal sealed class BeatmapPreviewMaker(
	IOptions<AssetOptions> options,
	IBeatmapsetStorage storage,
	ILogger<BeatmapPreviewMaker> logger) : IBeatmapAssets
{
	private const int PreviewSeconds = 10;
	private const int FadeDurationSeconds = 1;

	public async Task BuildAsync(BeatmapsetRecord set, IReadOnlyList<BeatmapRecord> beatmaps)
	{
		var previews = new Dictionary<string, TimeSpan?>(StringComparer.OrdinalIgnoreCase);
		foreach (var beatmap in beatmaps)
		{
			var audio = beatmap.Assets.AudioFile;
			if (!string.IsNullOrEmpty(audio)) previews.TryAdd(audio, beatmap.Assets.PreviewTime);
		}

		foreach (var (audio, previewTime) in previews)
			try
			{
				await BuildPreviewAsync(set, audio, previewTime ?? TimeSpan.Zero);
			}
			catch (Exception exception)
			{
				logger.LogWarning(exception,
					"Audio preview build failed: BeatmapsetId={BeatmapsetId} Audio={Audio}", set.Id, audio);
			}
	}

	private async Task BuildPreviewAsync(BeatmapsetRecord set, string audio, TimeSpan start)
	{
		var source = await storage.OpenAsync(set, BeatmapsetFileType.Audio, audio);
		if (source is null) return;
		await using var sourceScope = source;

		var directory = options.Value.CacheDirectory;
		Directory.CreateDirectory(directory);

		var outputPath = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp.mp3");
		try
		{
			EncodePreview(source, outputPath, start, logger, set.Id, audio);

			var preview = File.OpenRead(outputPath);
			await using var previewScope = preview;
			await storage.SaveAsync(set, BeatmapsetFileType.Preview, audio, preview);
		}
		finally
		{
			if (File.Exists(outputPath)) File.Delete(outputPath);
		}
	}

	private static void EncodePreview(Stream source, string outputPath, TimeSpan previewTime,
		ILogger logger, int beatmapsetId, string audio)
	{
		using var reader = new SoundFileReader(source);
		if (reader.TotalTime <= TimeSpan.Zero) return;

		if (logger.IsEnabled(LogLevel.Debug))
			logger.LogDebug(
				"Audio track read: BeatmapsetId={BeatmapsetId} Audio={Audio} Duration={Duration}",
				beatmapsetId, audio, reader.TotalTime);

		// A preview point past the end of the track starts the preview at the beginning.
		var start = previewTime >= reader.TotalTime ? TimeSpan.Zero : previewTime;
		reader.CurrentTime = start;

		var fadeDuration = TimeSpan.FromSeconds(FadeDurationSeconds);
		var remaining = reader.TotalTime - start;
		var previewDuration = remaining < TimeSpan.FromSeconds(PreviewSeconds)
			? remaining
			: TimeSpan.FromSeconds(PreviewSeconds);
		var fadeStart = previewDuration > fadeDuration
			? previewDuration - fadeDuration
			: TimeSpan.Zero;

		var trimmed = reader.Take(TimeSpan.FromSeconds(PreviewSeconds));
		var faded = fadeStart > TimeSpan.Zero
			? new FadeOutSampleProvider(trimmed, fadeStart, fadeDuration)
			: trimmed;

		SoundFileWriter.CreateSoundFile(outputPath, faded.ToWaveProvider(),
			SoundFileMajorFormat.Mp3, null);
	}
}
