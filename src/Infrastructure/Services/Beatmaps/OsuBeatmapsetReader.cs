using System.IO.Compression;
using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Domain.Mechanics;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using LazerBeatmap = osu.Game.Beatmaps.Beatmap;

namespace Basil.Infrastructure.Services.Beatmaps;

/// <summary>Reads a beatmapset archive using ppy's osu!lazer legacy decoder.</summary>
internal sealed class OsuBeatmapsetReader : IBeatmapsetReader
{
	// ponytail: cap each difficulty at 16 MiB; raise this only if valid osu! difficulty files exceed it.
	private const int MaxOsuBytes = 16 * 1024 * 1024;

	/// <inheritdoc />
	/// <remarks>
	///     An archive that is not a valid zip, or that contains no decodable difficulty, yields
	///     <see langword="null" />. Blank artist, title, or creator resolve to <c>Unknown</c>, because
	///     the domain rejects blank values.
	/// </remarks>
	public Task<BeatmapsetArchive?> ReadAsync(Stream archive, CancellationToken cancellationToken = default)
	{
		LazerBeatmap? first = null;
		var difficulties = new List<BeatmapArchiveDifficulty>();

		ZipArchive zip;
		try
		{
			zip = new ZipArchive(archive, ZipArchiveMode.Read, true);
		}
		catch (InvalidDataException)
		{
			return Task.FromResult<BeatmapsetArchive?>(null);
		}

		using (zip)
		{
			foreach (var entry in zip.Entries)
			{
				if (!entry.Name.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)) continue;
				if (entry.Length > MaxOsuBytes) continue;

				using var buffer = new MemoryStream();
				using (var entryStream = entry.Open())
				{
					var chunk = new byte[81920];
					var remaining = MaxOsuBytes;
					while (remaining > 0)
					{
						var read = entryStream.Read(chunk, 0, Math.Min(chunk.Length, remaining));
						if (read == 0) break;
						buffer.Write(chunk, 0, read);
						remaining -= read;
					}
				}

				var bytes = buffer.ToArray();
				var decoded = TryDecode(bytes);
				if (decoded is null) continue;

				first ??= decoded;

				var info = decoded.BeatmapInfo;
				var onlineId = info.OnlineID > 0 ? info.OnlineID : (int?)null;
				var mode = (GameMode)info.Ruleset.OnlineID;
				difficulties.Add(new BeatmapArchiveDifficulty(onlineId, info.DifficultyName, mode, bytes));
			}
		}

		if (first is null) return Task.FromResult<BeatmapsetArchive?>(null);

		var meta = first.BeatmapInfo;
		var onlineSetId = meta.BeatmapSet?.OnlineID is > 0 ? meta.BeatmapSet.OnlineID : (int?)null;
		var artist = string.IsNullOrWhiteSpace(meta.Metadata.Artist) ? "Unknown" : meta.Metadata.Artist;
		var title = string.IsNullOrWhiteSpace(meta.Metadata.Title) ? "Unknown" : meta.Metadata.Title;
		var creator = string.IsNullOrWhiteSpace(meta.Metadata.Author.Username)
			? "Unknown"
			: meta.Metadata.Author.Username;

		return Task.FromResult<BeatmapsetArchive?>(new BeatmapsetArchive(
			onlineSetId, artist, title, creator, difficulties));
	}

	/// <summary>Decodes one <c>.osu</c> file, or <see langword="null" /> when it is malformed.</summary>
	private static LazerBeatmap? TryDecode(byte[] osuBytes)
	{
		try
		{
			using var stream = new MemoryStream(osuBytes);
			using var reader = new LineBufferedReader(stream, true);
			return Decoder.GetDecoder<LazerBeatmap>(reader).Decode(reader);
		}
		catch
		{
			// Skip malformed .osu files rather than aborting the whole scan.
			return null;
		}
	}
}