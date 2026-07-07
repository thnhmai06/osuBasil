using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;
using OpenOsuTournament.Bancho.Application.Configuration;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using Beatmap = OpenOsuTournament.Bancho.Domain.Beatmaps.Beatmap;
using LazerBeatmap = osu.Game.Beatmaps.Beatmap;

namespace OpenOsuTournament.Bancho.Infrastructure.Beatmaps;

/// <summary>
///     New for this server's fully-offline scope — bancho.py always fetched beatmap metadata from
///     osu!api, so it has no local ".osu file on disk" concept at all. Scans
///     <see cref="StorageOptions.MapsetsPath" /> for ".osz" archives (unzipped in place, then
///     deleted) and loose ".osu" files (parsed, registered, renamed to "{beatmapId}.osu"), using
///     ppy.osu.Game's own decoder (the same one <see cref="Performance.PpyBeatmapDifficultyCalculator" />
///     uses) so metadata parsing matches the real client byte-for-byte.
/// </summary>
public sealed class BeatmapIngestionService(
    IMapRepository maps,
    IMapsetRepository mapsets,
    IOptions<StorageOptions> options)
{
    // ponytail: real osu! online ids are still well under this in 2026; a private-server-only
    // local id range this far up the int32 space keeps collisions with real ids implausible
    // without needing a dedicated id-space reservation table.
    private const int LocalIdFloor = 900_000_000;

    private static readonly char[] IllegalFilenameChars = [':', '\\', '/', '*', '<', '>', '?', '"', '|'];
    private static readonly Regex CanonicalFilenamePattern = new(@"^\d+\.osu$", RegexOptions.Compiled);

    /// <summary>Returns the number of beatmaps newly ingested this pass.</summary>
    public async Task<int> IngestAsync(CancellationToken cancellationToken = default)
    {
        var path = options.Value.MapsetsPath;
        Directory.CreateDirectory(path);

        var nextBeatmapId = Math.Max(LocalIdFloor, await maps.FetchMaxIdAsync(cancellationToken) + 1);
        var nextSetId = Math.Max(LocalIdFloor, await mapsets.FetchMaxIdAsync(cancellationToken) + 1);
        var ingested = 0;

        foreach (var oszPath in Directory.EnumerateFiles(path, "*.osz"))
        {
            int? setId = null;
            using (var archive = ZipFile.OpenRead(oszPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!entry.Name.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)) continue;

                    using var entryStream = entry.Open();
                    using var buffer = new MemoryStream();
                    await entryStream.CopyToAsync(buffer, cancellationToken);
                    var osuBytes = buffer.ToArray();

                    var parsed = TryDecode(osuBytes);
                    if (parsed is null) continue;

                    setId ??= AllocateSetId(parsed.BeatmapInfo.BeatmapSet?.OnlineID, ref nextSetId);
                    await mapsets.EnsureExistsAsync(setId.Value, cancellationToken);
                    var beatmapId = parsed.BeatmapInfo.OnlineID > 0 ? parsed.BeatmapInfo.OnlineID : nextBeatmapId++;
                    await IngestOneAsync(parsed, osuBytes, setId.Value, beatmapId, path, cancellationToken);
                    ingested++;
                }
            }

            File.Delete(oszPath);
        }

        foreach (var osuPath in Directory.EnumerateFiles(path, "*.osu"))
        {
            if (CanonicalFilenamePattern.IsMatch(Path.GetFileName(osuPath))) continue;

            var osuBytes = await File.ReadAllBytesAsync(osuPath, cancellationToken);
            var parsed = TryDecode(osuBytes);
            if (parsed is not null)
            {
                var setId = AllocateSetId(parsed.BeatmapInfo.BeatmapSet?.OnlineID, ref nextSetId);
                await mapsets.EnsureExistsAsync(setId, cancellationToken);
                var beatmapId = parsed.BeatmapInfo.OnlineID > 0 ? parsed.BeatmapInfo.OnlineID : nextBeatmapId++;
                await IngestOneAsync(parsed, osuBytes, setId, beatmapId, path, cancellationToken);
                ingested++;
            }

            File.Delete(osuPath);
        }

        return ingested;
    }

    private async Task IngestOneAsync(LazerBeatmap parsed, byte[] osuBytes, int setId, int beatmapId,
        string mapsetsPath, CancellationToken cancellationToken)
    {
        var info = parsed.BeatmapInfo;
        var md5 = Convert.ToHexString(MD5.HashData(osuBytes)).ToLowerInvariant();
        var filename = SanitizeFilename(
            $"{info.Metadata.Artist} - {info.Metadata.Title} ({info.Metadata.Author.Username}) [{info.DifficultyName}].osu");

        var beatmap = new Beatmap(
            md5, beatmapId, setId,
            info.Metadata.Artist, info.Metadata.Title, info.DifficultyName, info.Metadata.Author.Username,
            DateTime.UtcNow, (int)(info.Length / 1000), info.MaxCombo ?? 0,
            RankedStatus.Ranked, false, 0, 0,
            (GameMode)info.Ruleset.OnlineID,
            info.BPM, info.Difficulty.CircleSize, info.Difficulty.OverallDifficulty, info.Difficulty.ApproachRate,
            info.Difficulty.DrainRate, 0, filename);

        await maps.UpsertAsync(beatmap, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(mapsetsPath, $"{beatmapId}.osu"), osuBytes, cancellationToken);
    }

    private static int AllocateSetId(int? onlineSetId, ref int nextSetId)
    {
        return onlineSetId is > 0 ? onlineSetId.Value : nextSetId++;
    }

    private static LazerBeatmap? TryDecode(byte[] osuBytes)
    {
        try
        {
            using var stream = new MemoryStream(osuBytes);
            using var reader = new LineBufferedReader(stream);
            return Decoder.GetDecoder<LazerBeatmap>(reader).Decode(reader);
        }
        catch
        {
            // ponytail: skip malformed .osu files rather than aborting the whole scan.
            return null;
        }
    }

    private static string SanitizeFilename(string name)
    {
        foreach (var c in IllegalFilenameChars) name = name.Replace(c.ToString(), "");
        return name;
    }
}
