using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;

namespace Basil.Domain.Scores;

public sealed record ScoreSubmission
{
    #region Identity

    public required string BeatmapMd5 { get; init; }
    public required int UserId { get; init; }

    #endregion

    #region Mechanic

    public required GameMode Mode { get; init; }
    public required Mods Mods { get; init; }

    #endregion

    #region Stats

    public required HitCounts HitCounts { get; init; }
    public required long Score { get; init; }
    public required int MaxCombo { get; init; }
    public required Grade Grade { get; init; }
    public double Accuracy => HitCounts.CalculateAccuracy(Mode, Mods);

    public required bool IsPassed { get; init; }
    public required bool IsFullCombo { get; init; } // IsPerfect

    #endregion

    #region Synchronization

    public required DateTime ClientTime { get; init; }
    public DateTime ServerTime { get; init; } = DateTime.UtcNow;
    public TimeSpan TimeElapsed { get; init; }

    #endregion

    #region Integrity

    public ClientFlags ClientFlags { get; init; } = ClientFlags.Clean;
    public string ClientChecksum { get; init; } = string.Empty;

    #endregion

    /// <summary>
    ///     Ported from Score.from_submission. `fields` is the decrypted colon-delimited submission
    ///     string with the leading beatmap_md5/username entries already stripped by the caller (they
    ///     aren't score fields — see score_submission_beatmap_md5/score_submission_username).
    /// </summary>
    public static ScoreSubmission FromSubmission(IReadOnlyList<string> fields)
    {
        var mods = (Mods)int.Parse(fields[11], CultureInfo.InvariantCulture);

        return new ScoreSubmission
        {
            // Placeholders — the caller doesn't know the beatmap/player until after this parse (they're
            // stripped from `fields` before it reaches here); immediately overwritten via `with`.
            BeatmapMd5 = string.Empty,
            UserId = 0,
            ClientChecksum = fields[0],
            HitCounts = new HitCounts(
                x300: int.Parse(fields[1], CultureInfo.InvariantCulture),
                x100: int.Parse(fields[2], CultureInfo.InvariantCulture),
                x50: int.Parse(fields[3], CultureInfo.InvariantCulture),
                xGeki: int.Parse(fields[4], CultureInfo.InvariantCulture),
                xKatu: int.Parse(fields[5], CultureInfo.InvariantCulture),
                xMiss: int.Parse(fields[6], CultureInfo.InvariantCulture)),
            Score = long.Parse(fields[7], CultureInfo.InvariantCulture),
            MaxCombo = int.Parse(fields[8], CultureInfo.InvariantCulture),
            IsFullCombo = fields[9] == "True",
            Grade = Enum.Parse<Grade>(fields[10], ignoreCase: true),
            Mods = mods,
            IsPassed = fields[12] == "True",
            Mode = (GameMode)int.Parse(fields[13], CultureInfo.InvariantCulture),
            ClientTime = DateTime.ParseExact(fields[14], "yyMMddHHmmss", CultureInfo.InvariantCulture),
            ClientFlags = (ClientFlags)(fields[15].Count(c => c == ' ') & ~4)
        };
    }
    /// <summary>
    ///     Ported from Score.compute_online_checksum. The exact format string (and field order, which
    ///     does not match the format-arg index order — storyboardChecksum is placed before osuVersion
    ///     in the source template) must be preserved byte-for-byte since it's an interop checksum
    ///     verified against the osu! client's own computation.
    /// </summary>
    public string ComputeOnlineChecksum(string playerName, string osuVersion, string osuClientHash,
        string storyboardChecksum)
    {
        var raw =
            $"chickenmcnuggets{HitCounts.x100 + HitCounts.x300}o15{HitCounts.x50}{HitCounts.xGeki}" +
            $"smustard{HitCounts.xKatu}{HitCounts.xMiss}uu{BeatmapMd5}{MaxCombo}" +
            $"{IsFullCombo}{playerName}{Score}{Grade}{(int)Mods}Q{IsPassed}{(int)Mode}" +
            $"{osuVersion}{ClientTime:yyMMddHHmmss}{osuClientHash}{storyboardChecksum}";

        var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Ported from validate_online_score_checksum.</summary>
    public void ValidateScoreChecksum(string playerName, string osuVersion, string clientHash, string? storyboardMd5)
    {
        var serverChecksum = ComputeOnlineChecksum(playerName, osuVersion, clientHash, storyboardMd5 ?? "");
        if (ClientChecksum != serverChecksum)
            throw new ScoreSubmissionIntegrityException(
                $"online score checksum mismatch ({serverChecksum} != {ClientChecksum})");
    }

    /// <summary>
    ///     Ported from app/services/score_submission.py's validate_submission_integrity.
    ///     bancho.py currently treats every failure here as non-fatal (logged + a metric, restriction
    ///     disabled pending a trial period) — callers decide whether to enforce these, this only
    ///     reports the mismatch.
    /// </summary>
    public void ValidateSubmissionIntegrity(
        ClientDetails? clientDetails,
        DateOnly loginOsuVersionDate,
        string playerName,
        string osuVersion,
        string clientHash,
        string uniqueIds,
        string? storyboardMd5,
        string submissionBeatmapMd5,
        string updatedBeatmapHash)
    {
        var uniqueIdHashes = ParseUniqueIdHashes(uniqueIds);
        ValidateClientDetails(clientDetails, loginOsuVersionDate, osuVersion, clientHash, uniqueIdHashes);
        ValidateScoreChecksum(playerName, osuVersion, clientHash, storyboardMd5);
        ValidateBeatmapHash(submissionBeatmapMd5, updatedBeatmapHash);
    }

    /// <summary>Ported from parse_unique_id_hashes.</summary>
    public static UniqueIdHashes ParseUniqueIdHashes(string uniqueIds)
    {
        var parts = uniqueIds.Split('|', 2);
        return new UniqueIdHashes(Md5Hex(parts[0]), Md5Hex(parts[1]));
    }

    /// <summary>Ported from validate_client_details.</summary>
    public static void ValidateClientDetails(
        ClientDetails? clientDetails, DateOnly loginOsuVersionDate, string osuVersion, string clientHash,
        UniqueIdHashes uniqueIdHashes)
    {
        if (clientDetails is null) throw new ScoreSubmissionIntegrityException("missing client details");

        if (osuVersion != loginOsuVersionDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
            throw new ScoreSubmissionIntegrityException("osu! version mismatch");

        if (clientHash != clientDetails.Hash()) throw new ScoreSubmissionIntegrityException("client hash mismatch");

        if (uniqueIdHashes.UniqueId1Md5 != clientDetails.UninstallMd5)
            throw new ScoreSubmissionIntegrityException(
                $"unique_id1 mismatch ({uniqueIdHashes.UniqueId1Md5} != {clientDetails.UninstallMd5})");

        if (uniqueIdHashes.UniqueId2Md5 != clientDetails.DiskSignatureMd5)
            throw new ScoreSubmissionIntegrityException(
                $"unique_id2 mismatch ({uniqueIdHashes.UniqueId2Md5} != {clientDetails.DiskSignatureMd5})");
    }

    /// <summary>Ported from validate_replay_beatmap_hash.</summary>
    public static void ValidateBeatmapHash(string submissionBeatmapMd5, string updatedBeatmapHash)
    {
        if (submissionBeatmapMd5 != updatedBeatmapHash)
            throw new ScoreSubmissionIntegrityException(
                $"beatmap hash mismatch ({submissionBeatmapMd5} != {updatedBeatmapHash})");
    }

    private static string Md5Hex(string value)
    {
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    }
}

/// <summary>
///     Ported from app/services/score_submission.py's validation failure, as a distinct type so callers can catch it
///     narrowly.
/// </summary>
public sealed class ScoreSubmissionIntegrityException(string message) : Exception(message);

/// <summary>Ported from app/services/score_submission.py's UniqueIdHashes.</summary>
public sealed record UniqueIdHashes(string UniqueId1Md5, string UniqueId2Md5);