using Basil.Application.Capabilities.Beatmaps;
using System.IO.Compression;
using System.Text;
using Basil.Application.Persistence.Beatmaps;
using Basil.Domain.Mechanics;
using osu.Game.IO;
using Decoder = osu.Game.Beatmaps.Formats.Decoder;
using LazerBeatmap = osu.Game.Beatmaps.Beatmap;

namespace Basil.Infrastructure.Capabilities.Beatmaps;

/// <summary>Reads what a beatmapset archive contains, with ppy's osu!lazer legacy decoder.</summary>
internal sealed class OsuBeatmapsetReader : IBeatmapsetReader
{
	// ponytail: at most 2,000 files per set; raise this only if legitimate osu! sets exceed it.
	private const int MaxFiles = 2_000;

	// ponytail: at most 2 GiB unpacked per set; raise this only if legitimate osu! sets exceed it.
	private const long MaxTotalBytes = 2L * 1024 * 1024 * 1024;

	// ponytail: cap each difficulty at 16 MiB; raise this only if valid osu! difficulty files exceed it.
	private const int MaxOsuBytes = 16 * 1024 * 1024;

	/// <inheritdoc />
	/// <remarks>
	///     Blank artist, title, or creator resolve to <c>Unknown</c>, because the domain rejects blank values.
	/// </remarks>
	public async Task<BeatmapsetContent?> ReadAsync(Stream archive, CancellationToken cancellationToken = default)
	{
		ZipArchive zip;
		try
		{
			zip = await ZipArchive.CreateAsync(archive, ZipArchiveMode.Read, true,
				null, cancellationToken);
		}
		catch (InvalidDataException)
		{
			return null;
		}

		var keepArchive = false;
		try
		{
			var files = new List<BeatmapsetContentFile>();
			files.AddRange(zip.Entries
				.Where(entry => !entry.FullName.EndsWith('/') && !entry.FullName.EndsWith('\\'))
				.Select(entry => new ZipBeatmapsetContentFile(entry, entry.FullName.Replace('\\', '/'))));

			if (!IsAcceptable(files)) return null;

			var content = await ReadContentAsync(files, zip, cancellationToken);
			keepArchive = content is not null;
			return content;
		}
		finally
		{
			if (!keepArchive) await zip.DisposeAsync();
		}
	}

	/// <summary>Decodes the difficulties of a set's files and assembles what the set contains.</summary>
	private static async Task<BeatmapsetContent?> ReadContentAsync(IReadOnlyList<BeatmapsetContentFile> files,
		IAsyncDisposable? source, CancellationToken cancellationToken)
	{
		var byName = new Dictionary<string, BeatmapsetContentFile>(StringComparer.OrdinalIgnoreCase);
		foreach (var file in files) byName.TryAdd(Normalise(file.Name), file);

		LazerBeatmap? first = null;
		var difficulties = new List<BeatmapsetDifficulty>();

		foreach (var file in files)
		{
			// Difficulties lie at the top of a set, as its storyboard does; an archive with them deeper is no beatmapset.
			if (file.Name.Contains('/') || !file.Name.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)) continue;
			if (file.Length > MaxOsuBytes) continue;

			var decoded = await TryDecodeAsync(file, cancellationToken);
			if (decoded is null) continue;

			first ??= decoded.Beatmap;

			var info = decoded.Beatmap.BeatmapInfo;
			var metadata = info.Metadata;
			difficulties.Add(new BeatmapsetDifficulty(
				info.OnlineID > 0 ? info.OnlineID : null, info.DifficultyName, (GameMode)info.Ruleset.OnlineID, file,
				FindFile(byName, metadata.BackgroundFile), FindFile(byName, metadata.AudioFile),
				FindFile(byName, decoded.Video),
				metadata.PreviewTime >= 0 ? TimeSpan.FromMilliseconds(metadata.PreviewTime) : null,
				decoded.Storyboard));
		}

		if (first is null) return null;

		var meta = first.BeatmapInfo;
		var onlineSetId = meta.BeatmapSet?.OnlineID is > 0 ? meta.BeatmapSet.OnlineID : (int?)null;
		var artist = string.IsNullOrWhiteSpace(meta.Metadata.Artist) ? "Unknown" : meta.Metadata.Artist;
		var title = string.IsNullOrWhiteSpace(meta.Metadata.Title) ? "Unknown" : meta.Metadata.Title;
		var creator = string.IsNullOrWhiteSpace(meta.Metadata.Author.Username)
			? "Unknown"
			: meta.Metadata.Author.Username;

		var storyboard = files.FirstOrDefault(file =>
			!file.Name.Contains('/') && file.Name.EndsWith(".osb", StringComparison.OrdinalIgnoreCase));

		return new BeatmapsetContent(onlineSetId, artist, title, creator, difficulties, files, storyboard, source);
	}

	/// <summary>Decodes one <c>.osu</c> file, or <see langword="null" /> when it is malformed.</summary>
	private static async Task<DecodedDifficulty?> TryDecodeAsync(BeatmapsetContentFile file,
		CancellationToken cancellationToken)
	{
		try
		{
			var content = await file.OpenAsync(cancellationToken);
			await using var contentScope = content;
			using var buffer = new MemoryStream();
			var chunk = new byte[81920];
			var read = 0;
			while (read <= MaxOsuBytes)
			{
				var count = await content.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, MaxOsuBytes + 1 - read)),
					cancellationToken);
				if (count == 0) break;
				buffer.Write(chunk, 0, count);
				read += count;
			}

			if (read > MaxOsuBytes) return null;

			buffer.Position = 0;
			LazerBeatmap beatmap;
			using (var reader = new LineBufferedReader(buffer, true))
			{
				beatmap = Decoder.GetDecoder<LazerBeatmap>(reader).Decode(reader);
			}

			var (video, storyboard) = await ReadEventsAsync(buffer, cancellationToken);
			return new DecodedDifficulty(beatmap, video, storyboard);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// Skip malformed .osu files rather than aborting the whole scan.
			return null;
		}
	}

	/// <summary>
	///     Reads the video a <c>.osu</c> file names in its <c>[Events]</c> section (<see langword="null" /> when it names
	///     none) and whether that section holds a storyboard object.
	/// </summary>
	private static async Task<(string? Video, bool Storyboard)> ReadEventsAsync(Stream content,
		CancellationToken cancellationToken)
	{
		content.Position = 0;
		using var reader = new StreamReader(content, Encoding.UTF8, true,
			1024, true);
		var section = "";
		string? video = null;
		var storyboard = false;
		while (await reader.ReadLineAsync(cancellationToken) is { } line)
		{
			if (line.StartsWith('[') && line.EndsWith(']'))
			{
				section = line;
				continue;
			}

			if (section != "[Events]") continue;

			video ??= FirstQuoted(line, "Video,") ?? FirstQuoted(line, "1,");
			var trimmed = line.AsSpan().TrimStart();
			storyboard |= trimmed.StartsWith("Sprite,", StringComparison.Ordinal) ||
			              trimmed.StartsWith("Animation,", StringComparison.Ordinal);
		}

		return (video, storyboard);
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

	/// <summary>Finds the file a difficulty names, or <see langword="null" /> when the set has no such file.</summary>
	private static BeatmapsetContentFile? FindFile(Dictionary<string, BeatmapsetContentFile> files, string? name)
	{
		return string.IsNullOrEmpty(name) ? null : files.GetValueOrDefault(Normalise(name));
	}

	/// <summary>Replaces backslashes with forward slashes.</summary>
	private static string Normalise(string name)
	{
		return name.Replace('\\', '/');
	}

	/// <summary>
	///     Whether a set's files are acceptable: every name is safe and unique ignoring case, and the set stays within
	///     the file count and total size limits.
	/// </summary>
	private static bool IsAcceptable(IReadOnlyList<BeatmapsetContentFile> files)
	{
		if (files.Count > MaxFiles) return false;

		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var totalBytes = 0L;
		foreach (var file in files)
		{
			if (!IsSafeName(file.Name) || !names.Add(file.Name) ||
			    totalBytes + file.Length > MaxTotalBytes)
				return false;

			totalBytes += file.Length;
		}

		return true;
	}

	/// <summary>Whether a set-relative file name stays inside the set's own folder.</summary>
	private static bool IsSafeName(string name)
	{
		if (string.IsNullOrEmpty(name)) return false;
		if (Path.IsPathRooted(name)) return false;
		if (name.Contains(':', StringComparison.Ordinal)) return false;
		return !name.Split('/').Any(segment => segment is "" or "." or "..");
	}

	/// <summary>A decoded difficulty together with the video and storyboard its <c>.osu</c> file declares.</summary>
	/// <param name="Beatmap">The decoded difficulty.</param>
	/// <param name="Video">The video's name, or <see langword="null" /> when the file names none.</param>
	/// <param name="Storyboard">Whether the file draws a storyboard of its own.</param>
	private sealed record DecodedDifficulty(LazerBeatmap Beatmap, string? Video, bool Storyboard);
}