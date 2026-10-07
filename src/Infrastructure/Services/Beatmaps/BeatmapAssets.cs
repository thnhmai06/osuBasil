using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.IO.Compression;
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
	IBeatmapsetStorage archives,
	IBeatmapRepository beatmaps,
	ILogger<BeatmapAssets> logger) : IBeatmapAssets
{
	/// <summary>Extensions whose entries are stripped from the no-video archive.</summary>
	private static readonly FrozenSet<string> VideoExtensions = new[]
	{
		".mp4", ".avi", ".flv", ".m4v", ".mkv", ".mov", ".mpg", ".mpeg", ".webm", ".wmv"
	}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>Per-folder single-flight lock guarding the cache creation.</summary>
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> FolderLocks = new(StringComparer.Ordinal);

	/// <inheritdoc />
	public async Task<Stream?> OpenAsync(Beatmap beatmap, BeatmapAsset asset, CancellationToken cancellationToken = default)
	{
		var set = beatmap.Value.Beatmapset;
		var archive = await archives.OpenAsync(set, cancellationToken);
		if (archive is null) return null;

		try
		{
			return asset switch
			{
				BeatmapAsset.File => await OpenBeatmapFileAsync(set, archive, beatmap, cancellationToken),
				BeatmapAsset.Background or BeatmapAsset.Audio or BeatmapAsset.Video
					=> await OpenBeatmapAssetAsync(set, archive, beatmap, asset, cancellationToken),
				_ => null
			};
		}
		finally
		{
			await archive.DisposeAsync();
		}
	}

	/// <inheritdoc />
	public async Task<Stream?> OpenAsync(Beatmapset set, BeatmapsetAsset asset, CancellationToken cancellationToken = default)
	{
		var archive = await archives.OpenAsync(set, cancellationToken);
		if (archive is null) return null;

		Stream? result = null;
		try
		{
			result = asset switch
			{
				BeatmapsetAsset.Archive => archive,
				BeatmapsetAsset.ArchiveWithoutVideo => await OpenArchiveWithoutVideoAsync(set, archive, cancellationToken),
				BeatmapsetAsset.AudioPreview => await OpenAudioPreviewAsync(set, archive, cancellationToken),
				BeatmapsetAsset.Background or BeatmapsetAsset.Audio
					=> await OpenSetSharedAssetAsync(set, archive, asset, cancellationToken),
				BeatmapsetAsset.Storyboard => await OpenStoryboardAsync(set, archive, cancellationToken),
				_ => null
			};
		}
		finally
		{
			// The Archive asset is the archive stream itself; every other asset's result is
			// extracted to the cache, so the archive can be released here.
			if (!ReferenceEquals(result, archive)) await archive.DisposeAsync();
		}
		return result;
	}

	/// <summary>Opens the background, audio, or video of a single beatmap.</summary>
	private async Task<Stream?> OpenBeatmapAssetAsync(Beatmapset set, Stream archive, Beatmap beatmap,
		BeatmapAsset asset, CancellationToken cancellationToken)
	{
		var folder = await EnsureCacheFolderAsync(set, cancellationToken);
		var info = await ReadBeatmapInfoAsync(archive, beatmap.Value.Hash, cancellationToken);
		if (info is null) return null;

		var name = asset switch
		{
			BeatmapAsset.Background => info.BackgroundFile,
			BeatmapAsset.Audio => info.AudioFile,
			BeatmapAsset.Video => info.VideoFile,
			_ => null
		};
		if (string.IsNullOrEmpty(name)) return null;

		return await ExtractEntryAsync(archive, name, folder, cancellationToken);
	}

	/// <summary>Opens the background or audio of a beatmapset, as the lowest-id beatmap declares them.</summary>
	private async Task<Stream?> OpenSetSharedAssetAsync(Beatmapset set, Stream archive, BeatmapsetAsset asset,
		CancellationToken cancellationToken)
	{
		var primary = await LowestIdBeatmapAsync(set, cancellationToken);
		if (primary is null) return null;

		var folder = await EnsureCacheFolderAsync(set, cancellationToken);
		var info = await ReadBeatmapInfoAsync(archive, primary.Value.Hash, cancellationToken);
		if (info is null) return null;

		var name = asset switch
		{
			BeatmapsetAsset.Background => info.BackgroundFile,
			BeatmapsetAsset.Audio => info.AudioFile,
			_ => null
		};
		if (string.IsNullOrEmpty(name)) return null;

		return await ExtractEntryAsync(archive, name, folder, cancellationToken);
	}

	/// <summary>Reads the <c>.osu</c> file the beatmap with <paramref name="hash" /> lives in.</summary>
	private async Task<OsuInfo?> ReadBeatmapInfoAsync(Stream archive, Md5 hash, CancellationToken cancellationToken)
	{
		await using var osuStream = await OpenEntryByHashAsync(archive, hash, cancellationToken);
		if (osuStream is null) return null;

		using var reader = new StreamReader(osuStream);
		return ParseOsu(await reader.ReadToEndAsync(cancellationToken));
	}

	/// <summary>Opens the <c>.osu</c> file inside the archive whose content hashes to <paramref name="hash" />.</summary>
	private static async Task<Stream?> OpenEntryByHashAsync(Stream archive, Md5 hash, CancellationToken cancellationToken)
	{
		using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
		foreach (var entry in zip.Entries)
		{
			if (!string.Equals(Path.GetExtension(entry.FullName), ".osu", StringComparison.OrdinalIgnoreCase)) continue;

			await using var stream = await entry.OpenAsync(cancellationToken);
			var memory = new MemoryStream();
			try
			{
				await stream.CopyToAsync(memory, cancellationToken);
				if (new Md5(memory.ToArray()) == hash)
				{
					memory.Position = 0;
					return memory;
				}
			}
			catch
			{
				await memory.DisposeAsync();
				throw;
			}

			await memory.DisposeAsync();
		}

		return null;
	}

	/// <summary>Opens a beatmap's <c>.osu</c> file, cached under the beatmap's hash.</summary>
	private async Task<Stream?> OpenBeatmapFileAsync(Beatmapset set, Stream archive, Beatmap beatmap,
		CancellationToken cancellationToken)
	{
		var folder = await EnsureCacheFolderAsync(set, cancellationToken);
		var path = Path.Combine(folder, $"{beatmap.Value.Hash.HashValue}.osu");
		if (File.Exists(path)) return File.OpenRead(path);

		await using var stream = await OpenEntryByHashAsync(archive, beatmap.Value.Hash, cancellationToken);
		if (stream is null) return null;

		var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
		try
		{
			await using (var fileStream = File.Create(tempPath))
				await stream.CopyToAsync(fileStream, cancellationToken);

			File.Move(tempPath, path, true);
		}
		catch
		{
			if (File.Exists(tempPath)) File.Delete(tempPath);
			throw;
		}

		return File.OpenRead(path);
	}

	/// <summary>Extracts the entry matching <paramref name="entryName" /> (case-insensitive) to the cache folder.</summary>
	private async Task<Stream?> ExtractEntryAsync(Stream archive, string entryName, string folder,
		CancellationToken cancellationToken)
	{
		if (!IsSafeEntryName(entryName)) return null;

		using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
		ZipArchiveEntry? match = null;
		foreach (var entry in zip.Entries)
			if (string.Equals(Normalise(entry.FullName), Normalise(entryName), StringComparison.OrdinalIgnoreCase))
			{
				match = entry;
				break;
			}

		if (match is null) return null;

		var path = Path.Combine(folder, entryName);
		if (File.Exists(path)) return File.OpenRead(path);

		var parent = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

		var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
		try
		{
			await using (var entryStream = await match.OpenAsync(cancellationToken))
			await using (var fileStream = File.Create(tempPath))
				await entryStream.CopyToAsync(fileStream, cancellationToken);

			File.Move(tempPath, path, true);
		}
		catch
		{
			if (File.Exists(tempPath)) File.Delete(tempPath);
			throw;
		}

		return File.OpenRead(path);
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

	/// <summary>The first <c>*.osb</c> entry in the archive, copied to the cache folder.</summary>
	private async Task<Stream?> OpenStoryboardAsync(Beatmapset set, Stream archive,
		CancellationToken cancellationToken)
	{
		var folder = await EnsureCacheFolderAsync(set, cancellationToken);

		using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
		foreach (var entry in zip.Entries)
		{
			if (!string.Equals(Path.GetExtension(entry.FullName), ".osb", StringComparison.OrdinalIgnoreCase)) continue;
			if (!IsSafeEntryName(entry.Name)) continue;

			var path = Path.Combine(folder, entry.Name);
			if (File.Exists(path)) return File.OpenRead(path);

			var parent = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

			var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
			try
			{
				await using (var entryStream = await entry.OpenAsync(cancellationToken))
				await using (var fileStream = File.Create(tempPath))
					await entryStream.CopyToAsync(fileStream, cancellationToken);

				File.Move(tempPath, path, true);
			}
			catch
			{
				if (File.Exists(tempPath)) File.Delete(tempPath);
				throw;
			}

			return File.OpenRead(path);
		}

		return null;
	}

	/// <summary>Returns the lowest-id beatmap of the set, which carries the set's background and audio.</summary>
	private async Task<Beatmap?> LowestIdBeatmapAsync(Beatmapset set, CancellationToken cancellationToken)
	{
		var list = await beatmaps.ListAsync(set, cancellationToken);
		return list.Count == 0 ? null : list[0];
	}

	/// <summary>
	///     Creates the cache folder for the set's current <c>UpdatedAt</c>, deleting other
	///     <c>{set.Id}-*</c> folders so a re-import never serves stale files.
	/// </summary>
	private async Task<string> EnsureCacheFolderAsync(Beatmapset set, CancellationToken cancellationToken)
	{
		var setDir = Path.Combine(options.Value.CacheDirectory, "beatmapsets");
		var folder = Path.Combine(setDir, $"{set.Id}-{set.Value.UpdatedAt.ToUnixTimeMilliseconds()}");

		var gate = FolderLocks.GetOrAdd(folder, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			if (Directory.Exists(folder)) return folder;

			Directory.CreateDirectory(setDir);
			foreach (var stale in Directory.EnumerateDirectories(setDir, $"{set.Id}-*"))
				if (!string.Equals(stale, folder, StringComparison.Ordinal))
					Directory.Delete(stale, true);

			Directory.CreateDirectory(folder);
			return folder;
		}
		finally
		{
			gate.Release();
			FolderLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(folder, gate));
		}
	}

	/// <summary>A copy of the archive with video entries removed.</summary>
	private async Task<Stream> OpenArchiveWithoutVideoAsync(Beatmapset set, Stream archive,
		CancellationToken cancellationToken)
	{
		var folder = await EnsureCacheFolderAsync(set, cancellationToken);
		var path = Path.Combine(folder, "no-video.osz");
		if (File.Exists(path)) return File.OpenRead(path);

		var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
		try
		{
			using (var source = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true))
			await using (var dest = new ZipArchive(File.Create(tempPath), ZipArchiveMode.Create, false))
			{
				foreach (var entry in source.Entries)
				{
					if (string.IsNullOrEmpty(entry.Name)) continue;
					if (VideoExtensions.Contains(Path.GetExtension(entry.Name))) continue;

					var copy = dest.CreateEntry(entry.FullName);
					await using var entryStream = await entry.OpenAsync(cancellationToken);
					await using var copyStream = await copy.OpenAsync(cancellationToken);
					await entryStream.CopyToAsync(copyStream, cancellationToken);
				}
			}

			File.Move(tempPath, path, true);
		}
		catch
		{
			if (File.Exists(tempPath)) File.Delete(tempPath);
			throw;
		}

		return File.OpenRead(path);
	}

	/// <summary>A ten-second clip of the set's audio at the lowest-id beatmap's <c>PreviewTime</c>.</summary>
	private async Task<Stream?> OpenAudioPreviewAsync(Beatmapset set, Stream archive,
		CancellationToken cancellationToken)
	{
		var primary = await LowestIdBeatmapAsync(set, cancellationToken);
		if (primary is null) return null;

		var info = await ReadBeatmapInfoAsync(archive, primary.Value.Hash, cancellationToken);
		if (info is null || string.IsNullOrEmpty(info.AudioFile)) return null;

		var folder = await EnsureCacheFolderAsync(set, cancellationToken);
		var path = Path.Combine(folder, "preview.mp3");
		if (!File.Exists(path))
		{
			var audioPath = Path.Combine(folder, info.AudioFile);
			await using (var audio = await ExtractEntryAsync(archive, info.AudioFile, folder, cancellationToken))
			{
				if (audio is null) return null;
			}

			var startSeconds = Math.Max(info.PreviewTime, 0) / 1000d;
			try
			{
				await RunFfmpegAsync(audioPath, startSeconds, path, cancellationToken);
			}
			catch (FFMpegException e)
			{
				logger.LogWarning(
					"Audio preview extraction failed: BeatmapsetId={BeatmapsetId} Type={Type} Output={Output}",
					set.Id, e.Type, e.FFMpegErrorOutput);
				if (File.Exists(path)) File.Delete(path);
				return null;
			}
		}

		return File.OpenRead(path);
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