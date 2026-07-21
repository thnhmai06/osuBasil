using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Infrastructure.Performance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using Beatmap = Basil.Domain.Beatmaps.Beatmap;
using LazerBeatmap = osu.Game.Beatmaps.Beatmap;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>
///     Keeps the DB in sync with <see cref="StorageOptions.MapsetsPath" />: one subfolder per
///     mapset (<c>"{MapsetId} {Artist} - {Title}"</c>, holding the full original .osz contents —
///     audio/images/backgrounds/video/.osu — as extracted, not renamed), or a loose ".osz" at the
///     root waiting to be extracted. A single loose ".osu" file has no set context on its own and
///     is never ingested. Uses ppy.osu.Game's own decoder (the same one
///     <see cref="PpyDifficultyCalculator" /> uses) so metadata parsing matches the real client
///     byte-for-byte. bancho.py has no equivalent — it always fetched beatmap metadata from osu!api.
///     <see cref="BeatmapWatcherService" /> is the live caller (per-folder reconciliation on every
///     filesystem change); <see cref="ReconcileAllAsync" /> is the full-pass caller (server startup,
///     admin rescan).
/// </summary>
public sealed partial class BeatmapIngestionService(
    IMapRepository maps,
    IMapsetRepository mapsets,
    IDifficultyCalculator difficultyCalculator,
    IOptions<StorageOptions> options,
    ILogger<BeatmapIngestionService> logger)
{
    // Platform-provided, not a hardcoded Windows list — on Linux (a supported deploy target, see
    // CLAUDE.md's win-x64/linux-x64 publish commands) this is just { '\0', '/' }, so e.g. "|" in a
    // beatmap's title is a perfectly valid path character there and is left alone.
    private static readonly char[] IllegalFilenameChars = Path.GetInvalidFileNameChars();
    private static readonly Regex LeadingIdPattern = MyRegex();

    public static string MapsetFolderName(Mapset mapset)
    {
        return Sanitize($"{mapset.Id} {mapset.Artist} - {mapset.Title}");
    }

    public static string MapsetFolderPath(StorageOptions storage, Mapset mapset)
    {
        return Path.Combine(storage.MapsetsPath, MapsetFolderName(mapset));
    }

    /// <summary>
    ///     Resolves a mapset's actual folder on disk by leading-id prefix rather than recomputing
    ///     <see cref="MapsetFolderPath" /> from the mapset's current (mutable) Artist/Title — those
    ///     can drift from whatever the folder was actually named at ingestion time (e.g. a re-ingest
    ///     that revised the parsed title, or a folder that arrived pre-extracted with different
    ///     illegal-character handling than <see cref="Sanitize" /> uses), which would otherwise make
    ///     an existing, perfectly good folder invisible to every read path. Null if no folder with
    ///     that leading id exists.
    /// </summary>
    public static string? FindMapsetFolder(StorageOptions storage, int mapsetId)
    {
        if (!Directory.Exists(storage.MapsetsPath)) return null;
        return Directory.EnumerateDirectories(storage.MapsetsPath, $"{mapsetId} *").FirstOrDefault();
    }

    public static string? OsuFilePath(StorageOptions storage, Beatmap beatmap)
    {
        var folder = FindMapsetFolder(storage, beatmap.Mapset.Id);
        return folder is null ? null : Path.Combine(folder, beatmap.Filename);
    }

    /// <summary>
    ///     Full pass: extracts every loose ".osz" at the storage root, reconciles every subfolder
    ///     that looks like a mapset (".osu" files at depth 1), then deletes any Mapset row whose
    ///     folder no longer exists on disk. Run at server startup and by the admin rescan endpoint.
    ///     Returns the number of beatmaps added/updated this pass.
    /// </summary>
    public async Task<int> ReconcileAllAsync(CancellationToken cancellationToken = default)
    {
        var path = options.Value.MapsetsPath;
        Directory.CreateDirectory(path);

        var ingested = 0;
        var seenSetIds = new HashSet<int>();
        // Folders present before osz extraction — a folder extracted by ReconcileOszAsync below
        // is already reconciled internally, so the second loop must skip it to avoid double-counting.
        var preExistingFolders = Directory.EnumerateDirectories(path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var oszPath in Directory.EnumerateFiles(path, "*.osz"))
        {
            var (count, setId) = await ReconcileOszAsync(oszPath, cancellationToken);
            ingested += count;
            if (setId is not null) seenSetIds.Add(setId.Value);
        }

        foreach (var folder in Directory.EnumerateDirectories(path))
        {
            if (!preExistingFolders.Contains(folder)) continue;

            var (count, setId) = await ReconcileFolderAsync(folder, cancellationToken);
            ingested += count;
            if (setId is not null) seenSetIds.Add(setId.Value);
        }

        // A loose .osu file has no set context on its own (no sibling assets, no folder name to
        // derive Artist/Title from) — this pathway is deliberately not supported.
        foreach (var strayOsu in Directory.EnumerateFiles(path, "*.osu"))
            logger.LogWarning(
                "Ignoring stray .osu file at Mapsets root: {Path}. A single .osu has no set context — " +
                "drop a full .osz instead, or place it inside its own mapset folder.", strayOsu);

        var known = await mapsets.FetchAllIdsAsync(cancellationToken);
        foreach (var orphanId in known.Where(id => !seenSetIds.Contains(id)))
            await mapsets.DeleteAsync(orphanId, cancellationToken);

        return ingested;
    }

    /// <summary>Extracts every entry of a loose ".osz" (audio/images/video/.osu, as-is) into its resolved mapset folder, then reconciles that folder.</summary>
    public async Task<(int Ingested, int? SetId)> ReconcileOszAsync(string oszPath, CancellationToken cancellationToken = default)
    {
        var decoded = new List<DecodedFile>();
        await using (var archive = ZipFile.OpenRead(oszPath))
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.Name.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)) continue;

                await using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                await entryStream.CopyToAsync(buffer, cancellationToken);
                var osuBytes = buffer.ToArray();

                var parsed = TryDecode(osuBytes);
                if (parsed is null) continue;

                decoded.Add(new DecodedFile(entry.Name, osuBytes, HashMd5(osuBytes), parsed));
            }
        }

        if (decoded.Count == 0)
        {
            File.Delete(oszPath);
            return (0, null);
        }

        var mapset = await ResolveMapsetAsync(decoded, Path.GetFileNameWithoutExtension(oszPath), cancellationToken);
        var targetFolder = MapsetFolderPath(options.Value, mapset);
        Directory.CreateDirectory(targetFolder);

        await using (var archive = await ZipFile.OpenReadAsync(oszPath, cancellationToken))
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.Length == 0) continue; // directory entry

                var destination = Path.GetFullPath(Path.Combine(targetFolder, entry.FullName));
                if (!destination.StartsWith(targetFolder, StringComparison.OrdinalIgnoreCase))
                    continue; // zip-slip guard

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await entry.ExtractToFileAsync(destination, true, cancellationToken);
            }
        }

        File.Delete(oszPath);

        var (count, _) = await ReconcileFolderAsync(targetFolder, cancellationToken);
        return (count, mapset.Id);
    }

    /// <summary>
    ///     Adds/updates/deletes beatmaps for one mapset folder to match its current ".osu" files at
    ///     depth 1. Returns 0 (and a null MapsetId) when the folder has no ".osu" files at all — not a
    ///     mapset folder.
    /// </summary>
    public async Task<(int Ingested, int? SetId)> ReconcileFolderAsync(string folderPath,
        CancellationToken cancellationToken = default)
    {
        var decoded = new List<DecodedFile>();
        foreach (var osuPath in Directory.EnumerateFiles(folderPath, "*.osu", SearchOption.TopDirectoryOnly))
        {
            var osuBytes = await File.ReadAllBytesAsync(osuPath, cancellationToken);
            var parsed = TryDecode(osuBytes);
            if (parsed is null) continue;

            decoded.Add(new DecodedFile(Path.GetFileName(osuPath), osuBytes, HashMd5(osuBytes), parsed));
        }

        if (decoded.Count == 0) return (0, null);

        var mapset = await ResolveMapsetAsync(decoded, Path.GetFileName(folderPath), cancellationToken);
        var ingested = 0;

        foreach (var file in decoded)
        {
            var existingByPath = await maps.FetchOneAsync(
                filename: file.OriginalFilename, setId: mapset.Id, includeFrozen: true,
                cancellationToken: cancellationToken);

            var info = file.Parsed.BeatmapInfo;
            var mode = (GameMode)info.Ruleset.OnlineID;
            // Content unchanged (same md5) and already has a cached rating (matches the "Sr > 0
            // means cached" convention /difficulty-rating already uses) -> skip recalculating on
            // every reconcile pass (server startup, watcher); otherwise compute it, which also
            // backfills any pre-existing row still sitting at the old default of 0.
            var sr = existingByPath is { Difficulty.Sr: > 0 } existing && existing.Md5 == file.Md5
                ? existing.Difficulty.Sr
                : TryCalculateStarRating(Path.Combine(folderPath, file.OriginalFilename), mode);
            var beatmap = new Beatmap(
                file.Md5,
                existingByPath?.Id ?? (info.OnlineID > 0 ? info.OnlineID : 0),
                mapset,
                info.DifficultyName,
                file.OriginalFilename,
                TimeSpan.FromMilliseconds(info.Length),
                info.MaxCombo ?? 0,
                existingByPath?.IsFrozen ?? false,
                existingByPath?.Plays ?? 0,
                existingByPath?.Passes ?? 0,
                new Difficulty(
                    mode, info.BPM, info.Difficulty.CircleSize,
                    info.Difficulty.ApproachRate, info.Difficulty.OverallDifficulty, info.Difficulty.DrainRate,
                    sr));

            await maps.UpsertAsync(beatmap, cancellationToken);
            ingested++;
        }

        var onDisk = decoded.Select(f => f.OriginalFilename).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var known = await maps.FetchAllBySetIdAsync(mapset.Id, includeFrozen: true, cancellationToken: cancellationToken);
        foreach (var gone in known.Where(k => !onDisk.Contains(k.Filename)))
            await maps.DeleteByMd5Async(gone.Md5, cancellationToken);

        return (ingested, mapset.Id);
    }

    /// <summary>A mapset folder vanished from disk — drop its DB row (Beatmaps cascade via FK) if its leading id is still parseable and known.</summary>
    public async Task ReconcileDeletedFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var match = LeadingIdPattern.Match(name);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var id)) return;

        if (await mapsets.FetchByIdAsync(id, cancellationToken) is not null)
            await mapsets.DeleteAsync(id, cancellationToken);
        // ponytail: a manually-renamed-away-from-convention folder that's then deleted leaves an
        // orphan row until the next ReconcileAllAsync pass reclaims it — acceptable for a
        // human-admin server.
    }

    /// <summary>
    ///     Resolves which Mapset a batch of decoded .osu files belongs to: content-hash match against
    ///     an existing Beatmap first (content identity wins over whatever the folder is currently
    ///     named), else a leading id parsed from the folder/file name if that Mapset still exists,
    ///     else a brand-new set (online MapsetId if the client embedded one, else a fresh local id).
    /// </summary>
    private async Task<Mapset> ResolveMapsetAsync(IReadOnlyList<DecodedFile> decoded, string folderOrFileName,
        CancellationToken cancellationToken)
    {
        foreach (var file in decoded)
        {
            var existing = await maps.FetchOneAsync(md5: file.Md5, includeFrozen: true, cancellationToken: cancellationToken);
            if (existing is not null) return existing.Mapset;
        }

        var match = LeadingIdPattern.Match(folderOrFileName);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var leadingId))
        {
            var existingMapset = await mapsets.FetchByIdAsync(leadingId, cancellationToken);
            if (existingMapset is not null) return await RefreshMapsetAsync(existingMapset, decoded[0], cancellationToken);
        }

        var onlineSetId = decoded[0].Parsed.BeatmapInfo.BeatmapSet?.OnlineID;
        var newId = onlineSetId is > 0
            ? onlineSetId.Value
            : Math.Max(Beatmap.LocalIdFloor, await mapsets.FetchMaxIdAsync(cancellationToken) + 1);

        return await RefreshMapsetAsync(null, decoded[0], cancellationToken, newId);
    }

    private async Task<Mapset> RefreshMapsetAsync(Mapset? existing, DecodedFile first, CancellationToken cancellationToken,
        int? newId = null)
    {
        var info = first.Parsed.BeatmapInfo;
        var now = DateTime.UtcNow;
        var mapset = new Mapset(
            existing?.Id ?? newId!.Value,
            info.Metadata.Artist,
            info.Metadata.Title,
            info.Metadata.Author.Username,
            now,
            existing?.CreatedAt ?? now);

        return await mapsets.UpsertAsync(mapset, cancellationToken);
    }

    private static string HashMd5(byte[] bytes)
    {
        return Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
    }

    private double TryCalculateStarRating(string osuFilePath, GameMode mode)
    {
        try
        {
            return difficultyCalculator.CalculateStarRating(osuFilePath, mode, Mods.NoMod);
        }
        catch (Exception e)
        {
            // ponytail: a map whose difficulty can't be calculated (unsupported ruleset content,
            // corrupt hitobjects) still gets ingested — it just keeps Sr at 0 instead of aborting.
            logger.LogWarning(e, "Failed to calculate star rating for {Path}.", osuFilePath);
            return 0;
        }
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

    private static string Sanitize(string name)
    {
        return IllegalFilenameChars.Aggregate(name, (current, c) => current.Replace(c.ToString(), ""));
    }

    private sealed record DecodedFile(string OriginalFilename, byte[] Bytes, string Md5, LazerBeatmap Parsed);

    [GeneratedRegex(@"^(\d+)")]
    private static partial Regex MyRegex();
}
