using System.IO.Compression;
using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Domain.Mechanics;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using LazerBeatmap = osu.Game.Beatmaps.Beatmap;

namespace Basil.Infrastructure.Services.Beatmaps;

/// <summary>Reads beatmapsets, from an archive or a folder, with ppy's osu!lazer legacy decoder.</summary>
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
			zip = await ZipArchive.CreateAsync(archive, ZipArchiveMode.Read, leaveOpen: true,
				entryNameEncoding: null, cancellationToken).ConfigureAwait(false);
		}
		catch (InvalidDataException)
		{
			return null;
		}

		var keepArchive = false;
		try
		{
			var files = new List<BeatmapsetFile>();
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var totalBytes = 0L;
			foreach (var entry in zip.Entries)
			{
				if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;

				var name = entry.FullName.Replace('\\', '/');
				if (!IsSafeName(name) || !names.Add(name) || files.Count >= MaxFiles ||
				    totalBytes + entry.Length > MaxTotalBytes)
					return null;

				totalBytes += entry.Length;
				files.Add(new ZipBeatmapsetFile(entry, name));
			}

			var content = await ReadContentAsync(files, zip, cancellationToken).ConfigureAwait(false);
			keepArchive = content is not null;
			return content;
		}
		finally
		{
			if (!keepArchive) await zip.DisposeAsync().ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	/// <remarks>
	///     Blank artist, title, or creator resolve to <c>Unknown</c>, because the domain rejects blank values.
	/// </remarks>
	public async Task<BeatmapsetContent?> ReadAsync(DirectoryInfo folder, CancellationToken cancellationToken = default)
	{
		if (!folder.Exists) return null;

		var files = new List<BeatmapsetFile>();
		var totalBytes = 0L;
		foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories))
		{
			var name = Path.GetRelativePath(folder.FullName, file.FullName).Replace('\\', '/');
			if (files.Count >= MaxFiles || totalBytes + file.Length > MaxTotalBytes) return null;

			totalBytes += file.Length;
			files.Add(new DiskBeatmapsetFile(file, name));
		}

		return await ReadContentAsync(files, null, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Decodes the difficulties of a set's files and assembles what the set contains.</summary>
	private static async Task<BeatmapsetContent?> ReadContentAsync(IReadOnlyList<BeatmapsetFile> files,
		IAsyncDisposable? source, CancellationToken cancellationToken)
	{
		LazerBeatmap? first = null;
		var difficulties = new List<BeatmapsetDifficulty>();

		foreach (var file in files)
		{
			if (!file.Name.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)) continue;
			if (file.Length > MaxOsuBytes) continue;

			var decoded = await TryDecodeAsync(file, cancellationToken).ConfigureAwait(false);
			if (decoded is null) continue;

			first ??= decoded;

			var info = decoded.BeatmapInfo;
			difficulties.Add(new BeatmapsetDifficulty(
				info.OnlineID > 0 ? info.OnlineID : null, info.DifficultyName, (GameMode)info.Ruleset.OnlineID, file));
		}

		if (first is null) return null;

		var meta = first.BeatmapInfo;
		var onlineSetId = meta.BeatmapSet?.OnlineID is > 0 ? meta.BeatmapSet.OnlineID : (int?)null;
		var artist = string.IsNullOrWhiteSpace(meta.Metadata.Artist) ? "Unknown" : meta.Metadata.Artist;
		var title = string.IsNullOrWhiteSpace(meta.Metadata.Title) ? "Unknown" : meta.Metadata.Title;
		var creator = string.IsNullOrWhiteSpace(meta.Metadata.Author.Username)
			? "Unknown"
			: meta.Metadata.Author.Username;

		return new BeatmapsetContent(onlineSetId, artist, title, creator, difficulties, files, source);
	}

	/// <summary>Decodes one <c>.osu</c> file, or <see langword="null" /> when it is malformed.</summary>
	private static async Task<LazerBeatmap?> TryDecodeAsync(BeatmapsetFile file, CancellationToken cancellationToken)
	{
		try
		{
			var content = await file.OpenAsync(cancellationToken).ConfigureAwait(false);
			await using var contentScope = content.ConfigureAwait(false);
			using var buffer = new MemoryStream();
			var chunk = new byte[81920];
			var read = 0;
			while (read <= MaxOsuBytes)
			{
				var count = await content.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, MaxOsuBytes + 1 - read)),
					cancellationToken).ConfigureAwait(false);
				if (count == 0) break;
				buffer.Write(chunk, 0, count);
				read += count;
			}

			if (read > MaxOsuBytes) return null;

			buffer.Position = 0;
			using var reader = new LineBufferedReader(buffer, true);
			return Decoder.GetDecoder<LazerBeatmap>(reader).Decode(reader);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// Skip malformed .osu files rather than aborting the whole scan.
			return null;
		}
	}

	/// <summary>Whether a set-relative file name stays inside the set's own folder.</summary>
	private static bool IsSafeName(string name)
	{
		if (string.IsNullOrEmpty(name)) return false;
		if (Path.IsPathRooted(name)) return false;
		if (name.Contains(':', StringComparison.Ordinal)) return false;
		foreach (var segment in name.Split('/'))
			if (segment is "" or "." or "..")
				return false;
		return true;
	}
}