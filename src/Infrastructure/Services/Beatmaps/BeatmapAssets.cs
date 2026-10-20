using Basil.Application.Objects.Beatmaps;
using Basil.Application.Storage.Beatmaps;
using Basil.Domain.Beatmaps;
using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Services.Beatmaps;

/// <summary>
///     Makes the files derived from a beatmapset's stored files: a ten-second audio preview of each audio track its
///     difficulties use, rendered through <c>ffmpeg</c>.
/// </summary>
internal sealed class BeatmapAssets(
	IOptions<AssetOptions> options,
	IBeatmapsetStorage storage,
	ILogger<BeatmapAssets> logger) : IBeatmapAssets
{
	/// <inheritdoc />
	public async Task BuildAsync(BeatmapsetRecord set, IReadOnlyList<BeatmapRecord> beatmaps,
		CancellationToken cancellationToken = default)
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
				await BuildPreviewAsync(set, audio, previewTime ?? TimeSpan.Zero, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception exception)
			{
				logger.LogWarning(exception,
					"Audio preview build failed: BeatmapsetId={BeatmapsetId} Audio={Audio}", set.Id, audio);
			}
	}

	/// <summary>Makes the preview of one audio track from the set's stored audio.</summary>
	/// <param name="set">The beatmapset, whose audio is already stored.</param>
	/// <param name="audio">The name of the audio track in the set.</param>
	/// <param name="start">Where the preview starts.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private async Task BuildPreviewAsync(BeatmapsetRecord set, string audio, TimeSpan start,
		CancellationToken cancellationToken)
	{
		var source = await storage.OpenAsync(set, BeatmapsetFileKind.Audio, audio, cancellationToken)
			.ConfigureAwait(false);
		if (source is null) return;
		await using var sourceScope = source.ConfigureAwait(false);

		var directory = options.Value.CacheDirectory;
		Directory.CreateDirectory(directory);
		var audioPath = Path.Combine(directory, $"{Guid.NewGuid():N}{Path.GetExtension(audio)}");
		var outputPath = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp.mp3");
		try
		{
			var file = new FileStream(audioPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
				81920, FileOptions.Asynchronous);
			await using (file.ConfigureAwait(false))
			{
				await source.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
			}

			await RunFfmpegAsync(audioPath, start, outputPath, cancellationToken).ConfigureAwait(false);

			var preview = File.OpenRead(outputPath);
			await using var previewScope = preview.ConfigureAwait(false);
			await storage.SaveAsync(set, BeatmapsetFileKind.Preview, audio, preview, cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			if (File.Exists(audioPath)) File.Delete(audioPath);
			if (File.Exists(outputPath)) File.Delete(outputPath);
		}
	}

	/// <summary>Cuts the ten-second preview clip with ffmpeg.</summary>
	private Task<bool> RunFfmpegAsync(string audioPath, TimeSpan start, string outputPath,
		CancellationToken cancellationToken)
	{
		return FFMpegArguments
			.FromFileInput(audioPath, true, input => input.Seek(start))
			.OutputToFile(outputPath, true, output => output
				.WithDuration(TimeSpan.FromSeconds(10))
				.DisableChannel(Channel.Video)
				.WithAudioCodec(AudioCodec.LibMp3Lame)
				.WithAudioBitrate(128)
				.WithCustomArgument("-af afade=t=out:st=9:d=1"))
			.CancellableThrough(cancellationToken)
			.ProcessAsynchronously(true, new FFOptions { BinaryFolder = options.Value.FfmpegFolder ?? string.Empty });
	}
}