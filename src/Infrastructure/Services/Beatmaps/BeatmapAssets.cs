using System.Collections.Concurrent;
using System.Globalization;
using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Utilities;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Services.Beatmaps;

/// <summary>
///     Opens the files that come with a beatmap or a beatmapset: the <c>.osu</c> difficulty, its
///     background, audio, and video, and for the set, its archive, storyboard and a ten-second audio
///     preview rendered through <c>ffmpeg</c>.
/// </summary>
internal sealed class BeatmapAssets(
	IOptions<AssetOptions> options,
	IBeatmapsetStorage storage,
	IBeatmapRepository beatmaps,
	ILogger<BeatmapAssets> logger) : IBeatmapAssets
{
	// ponytail: hashes the set's .osu files on every request; keep a hash → name index per set if it shows up in profiles.
	private readonly ConcurrentDictionary<string, Lazy<Task<bool>>> _previewBuilds = new(StringComparer.Ordinal);

	/// <inheritdoc />
	public async Task<Stream?> OpenAsync(Beatmap beatmap, BeatmapAsset asset,
		CancellationToken cancellationToken = default)
	{
		var set = beatmap.Value.Beatmapset;
		if (asset == BeatmapAsset.File)
		{
			var osuName = await OsuFileNameAsync(set, beatmap.Value.Hash, cancellationToken);
			return osuName is null ? null : await storage.OpenAsync(set, osuName, cancellationToken);
		}
		if (asset is not (BeatmapAsset.Background or BeatmapAsset.Audio or BeatmapAsset.Video)) return null;

		var info = await ReadBeatmapInfoAsync(set, beatmap.Value.Hash, cancellationToken);
		if (info is null) return null;
		var name = asset switch
		{
			BeatmapAsset.Background => info.BackgroundFile,
			BeatmapAsset.Audio => info.AudioFile,
			BeatmapAsset.Video => info.VideoFile,
			_ => null
		};
		return string.IsNullOrEmpty(name) || !IsSafeEntryName(name)
			? null
			: await storage.OpenAsync(set, name, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Stream?> OpenAsync(Beatmapset set, BeatmapsetAsset asset,
		CancellationToken cancellationToken = default)
	{
		return asset switch
		{
			BeatmapsetAsset.Archive => await storage.OpenArchiveAsync(set, withVideo: true, cancellationToken),
			BeatmapsetAsset.ArchiveWithoutVideo => await storage.OpenArchiveAsync(set, withVideo: false, cancellationToken),
			BeatmapsetAsset.AudioPreview => await OpenAudioPreviewAsync(set, cancellationToken),
			BeatmapsetAsset.Background or BeatmapsetAsset.Audio => await OpenSetSharedAssetAsync(set, asset, cancellationToken),
			BeatmapsetAsset.Storyboard => await OpenStoryboardAsync(set, cancellationToken),
			_ => null
		};
	}

	/// <inheritdoc />
	public Task ForgetAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var directory = options.Value.CacheDirectory;
		if (!Directory.Exists(directory)) return Task.CompletedTask;

		foreach (var path in Directory.EnumerateFiles(directory,
			         $"{set.Id.ToString(CultureInfo.InvariantCulture)}-*.mp3", SearchOption.TopDirectoryOnly))
		{
			cancellationToken.ThrowIfCancellationRequested();
			File.Delete(path);
		}
		return Task.CompletedTask;
	}

	/// <summary>Finds the difficulty file whose content has the stored hash.</summary>
	private async Task<string?> OsuFileNameAsync(Beatmapset set, Md5 hash, CancellationToken cancellationToken)
	{
		foreach (var name in await storage.ListAsync(set, cancellationToken))
		{
			if (!name.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)) continue;
			await using var stream = await storage.OpenAsync(set, name, cancellationToken);
			if (stream is null) continue;

			using var content = new MemoryStream();
			await stream.CopyToAsync(content, cancellationToken);
			if (new Md5(content.ToArray()) == hash) return name;
		}
		return null;
	}

	/// <summary>Reads the fields this asset layer needs from the difficulty matching <paramref name="hash" />.</summary>
	private async Task<OsuInfo?> ReadBeatmapInfoAsync(Beatmapset set, Md5 hash, CancellationToken cancellationToken)
	{
		var name = await OsuFileNameAsync(set, hash, cancellationToken);
		if (name is null) return null;
		await using var stream = await storage.OpenAsync(set, name, cancellationToken);
		if (stream is null) return null;
		using var reader = new StreamReader(stream);
		return ParseOsu(await reader.ReadToEndAsync(cancellationToken));
	}

	/// <summary>Opens the shared background or audio declared by the lowest-id beatmap in the set.</summary>
	private async Task<Stream?> OpenSetSharedAssetAsync(Beatmapset set, BeatmapsetAsset asset,
		CancellationToken cancellationToken)
	{
		var primary = await LowestIdBeatmapAsync(set, cancellationToken);
		if (primary is null) return null;
		var info = await ReadBeatmapInfoAsync(set, primary.Value.Hash, cancellationToken);
		if (info is null) return null;
		var name = asset == BeatmapsetAsset.Background ? info.BackgroundFile : info.AudioFile;
		return string.IsNullOrEmpty(name) || !IsSafeEntryName(name)
			? null
			: await storage.OpenAsync(set, name, cancellationToken);
	}

	/// <summary>Opens the first storyboard file in the set.</summary>
	private async Task<Stream?> OpenStoryboardAsync(Beatmapset set, CancellationToken cancellationToken)
	{
		var name = (await storage.ListAsync(set, cancellationToken))
			.FirstOrDefault(name => Path.GetExtension(name).Equals(".osb", StringComparison.OrdinalIgnoreCase));
		return name is null ? null : await storage.OpenAsync(set, name, cancellationToken);
	}

	/// <summary>A ten-second clip of the set's audio at the lowest-id beatmap's <c>PreviewTime</c>.</summary>
	private async Task<Stream?> OpenAudioPreviewAsync(Beatmapset set, CancellationToken cancellationToken)
	{
		var primary = await LowestIdBeatmapAsync(set, cancellationToken);
		if (primary is null) return null;
		var info = await ReadBeatmapInfoAsync(set, primary.Value.Hash, cancellationToken);
		if (info is null || string.IsNullOrEmpty(info.AudioFile) || !IsSafeEntryName(info.AudioFile)) return null;

		var path = Path.Combine(options.Value.CacheDirectory,
			$"{set.Id.ToString(CultureInfo.InvariantCulture)}-{set.Value.UpdatedAt.ToUnixTimeMilliseconds()}.mp3");
		if (File.Exists(path)) return File.OpenRead(path);

		var build = _previewBuilds.GetOrAdd(path, _ => new Lazy<Task<bool>>(
			() => BuildAudioPreviewAsync(set, info.AudioFile, Math.Max(info.PreviewTime, 0) / 1000d,
				path, cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication));
		try
		{
			return await build.Value ? File.OpenRead(path) : null;
		}
		finally
		{
			_previewBuilds.TryRemove(new KeyValuePair<string, Lazy<Task<bool>>>(path, build));
		}
	}

	private async Task<bool> BuildAudioPreviewAsync(Beatmapset set, string audioName, double startSeconds, string path,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(options.Value.CacheDirectory);
		var extension = Path.GetExtension(audioName);
		var audioPath = $"{path}.{Guid.NewGuid():N}.audio{extension}";
		var outputPath = $"{path}.{Guid.NewGuid():N}.tmp.mp3";
		try
		{
			await using (var audio = await storage.OpenAsync(set, audioName, cancellationToken))
			{
				if (audio is null) return false;
				await using var file = new FileStream(audioPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
					81920, FileOptions.Asynchronous);
				await audio.CopyToAsync(file, cancellationToken);
			}

			try
			{
				await RunFfmpegAsync(audioPath, startSeconds, outputPath, cancellationToken);
				File.Move(outputPath, path, overwrite: true);
				return true;
			}
			catch (FFMpegException exception)
			{
				logger.LogWarning(
					"Audio preview extraction failed: BeatmapsetId={BeatmapsetId} Type={Type} Output={Output}",
					set.Id, exception.Type, exception.FFMpegErrorOutput);
				return false;
			}
		}
		finally
		{
			if (File.Exists(audioPath)) File.Delete(audioPath);
			if (File.Exists(outputPath)) File.Delete(outputPath);
		}
	}

	/// <summary>Returns the lowest-id beatmap of the set, which carries the set's background and audio.</summary>
	private async Task<Beatmap?> LowestIdBeatmapAsync(Beatmapset set, CancellationToken cancellationToken)
	{
		var list = await beatmaps.ListAsync(set, cancellationToken);
		return list.Count == 0 ? null : list[0];
	}

	/// <summary>Returns true when <paramref name="name" /> is a relative path that stays inside its parent folder.</summary>
	private static bool IsSafeEntryName(string name)
	{
		if (string.IsNullOrEmpty(name)) return false;
		var normalised = Normalise(name);
		if (normalised.StartsWith('/') || normalised.StartsWith('\\')) return false;
		if (normalised.Contains("..", StringComparison.Ordinal)) return false;
		if (Path.IsPathRooted(name)) return false;
		if (name.Contains(':', StringComparison.Ordinal)) return false;
		return true;
	}

	/// <summary>Replaces backslashes with forward slashes.</summary>
	private static string Normalise(string name)
	{
		return name.Replace('\\', '/');
	}

	/// <summary>Cuts the ten-second preview clip with ffmpeg.</summary>
	private Task RunFfmpegAsync(string audioPath, double startSeconds, string outputPath,
		CancellationToken cancellationToken)
	{
		return FFMpegArguments
			.FromFileInput(audioPath, true, input => input.Seek(TimeSpan.FromSeconds(startSeconds)))
			.OutputToFile(outputPath, true, output => output
				.WithDuration(TimeSpan.FromSeconds(10))
				.DisableChannel(Channel.Video)
				.WithAudioCodec(AudioCodec.LibMp3Lame)
				.WithAudioBitrate(128)
				.WithCustomArgument("-af afade=t=out:st=9:d=1"))
			.CancellableThrough(cancellationToken)
			.ProcessAsynchronously(true, new FFOptions { BinaryFolder = options.Value.FfmpegFolder ?? string.Empty });
	}

	/// <summary>The fields a <c>.osu</c> file declares that the asset layer needs.</summary>
	/// <param name="AudioFile">The audio track's filename, or <see langword="null" /> when the file declares none.</param>
	/// <param name="PreviewTime">The audio preview offset in milliseconds, 0 when undeclared or negative.</param>
	/// <param name="BackgroundFile">The background image's filename, or <see langword="null" /> when none is declared.</param>
	/// <param name="VideoFile">The video's filename, or <see langword="null" /> when no video is declared.</param>
	private sealed record OsuInfo(string? AudioFile, int PreviewTime, string? BackgroundFile, string? VideoFile);

	/// <summary>Reads the small slice of <c>.osu</c> the asset layer needs.</summary>
	/// <remarks>
	///     <c>[General]</c>'s <c>AudioFilename:</c> and <c>PreviewTime:</c>, and <c>[Events]</c>'s
	///     background (first <c>0,0,&quot;...&quot;</c> line) and video (first <c>Video,</c> or <c>1,</c>
	///     line with a quoted file).
	/// </remarks>
	private static OsuInfo ParseOsu(string content)
	{
		string? audio = null;
		var preview = 0;
		string? background = null;
		string? video = null;

		var section = "";
		foreach (var rawLine in content.Split('\n'))
		{
			var line = rawLine.TrimEnd('\r');
			if (line.StartsWith('[') && line.EndsWith(']'))
			{
				section = line;
				continue;
			}

			if (section == "[General]")
			{
				if (audio is null && line.StartsWith("AudioFilename:", StringComparison.Ordinal))
					audio = Unquote(line["AudioFilename:".Length..].Trim());
				else if (line.StartsWith("PreviewTime:", StringComparison.Ordinal) &&
				         int.TryParse(line["PreviewTime:".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
					preview = t;
			}
			else if (section == "[Events]")
			{
				background ??= FirstQuoted(line, "0,0,");
				video ??= FirstQuoted(line, "Video,") ?? FirstQuoted(line, "1,");
			}
		}

		return new OsuInfo(audio, preview, background, video);
	}

	/// <summary>Strips a single pair of surrounding double quotes.</summary>
	private static string? Unquote(string value)
	{
		return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
			? value[1..^1]
			: value;
	}

	/// <summary>The quoted payload of <paramref name="line" /> when it begins with <paramref name="prefix" />.</summary>
	private static string? FirstQuoted(string line, string prefix)
	{
		if (!line.StartsWith(prefix, StringComparison.Ordinal)) return null;
		var first = line.IndexOf('"');
		var last = line.LastIndexOf('"');
		if (first < 0 || last <= first) return null;
		return line[(first + 1)..last];
	}
}
