using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenOsuTournament.Bancho.Domain.Login;

namespace OpenOsuTournament.Bancho.Domain.Scores;

/// <summary>
///     Ported from app/services/score_submission.py's validation failure, as a distinct type so callers can catch it
///     narrowly.
/// </summary>
public sealed class ScoreSubmissionIntegrityException(string message) : Exception(message);

/// <summary>Ported from app/services/score_submission.py's UniqueIdHashes.</summary>
public sealed record UniqueIdHashes(string UniqueId1Md5, string UniqueId2Md5);

/// <summary>
///     Ported from app/services/score_submission.py's parse_unique_id_hashes/validate_*/
///     validate_submission_integrity. bancho.py currently treats every failure here as non-fatal
///     (logged + a metric, restriction disabled pending a trial period) — callers decide whether to
///     enforce these, this class only reports the mismatch.
/// </summary>
public static class ScoreSubmissionValidation
{
    public static UniqueIdHashes ParseUniqueIdHashes(string uniqueIds)
    {
        var parts = uniqueIds.Split('|', 2);
        return new UniqueIdHashes(Md5Hex(parts[0]), Md5Hex(parts[1]));
    }

    public static void ValidateClientDetails(
        ClientDetails? clientDetails, string osuVersion, string clientHash, UniqueIdHashes uniqueIdHashes)
    {
        if (clientDetails is null) throw new ScoreSubmissionIntegrityException("missing client details");

        if (osuVersion != clientDetails.OsuVersionDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
            throw new ScoreSubmissionIntegrityException("osu! version mismatch");

        if (clientHash != clientDetails.ClientHash) throw new ScoreSubmissionIntegrityException("client hash mismatch");

        if (uniqueIdHashes.UniqueId1Md5 != clientDetails.UninstallMd5)
            throw new ScoreSubmissionIntegrityException(
                $"unique_id1 mismatch ({uniqueIdHashes.UniqueId1Md5} != {clientDetails.UninstallMd5})");

        if (uniqueIdHashes.UniqueId2Md5 != clientDetails.DiskSignatureMd5)
            throw new ScoreSubmissionIntegrityException(
                $"unique_id2 mismatch ({uniqueIdHashes.UniqueId2Md5} != {clientDetails.DiskSignatureMd5})");
    }

    public static void ValidateScoreChecksum(
        ScoreSubmission score, string osuVersion, string clientHash, string? storyboardMd5)
    {
        var serverChecksum = score.ComputeOnlineChecksum(osuVersion, clientHash, storyboardMd5 ?? "");
        if (score.ClientChecksum != serverChecksum)
            throw new ScoreSubmissionIntegrityException(
                $"online score checksum mismatch ({serverChecksum} != {score.ClientChecksum})");
    }

    public static void ValidateBeatmapHash(string submissionBeatmapMd5, string updatedBeatmapHash)
    {
        if (submissionBeatmapMd5 != updatedBeatmapHash)
            throw new ScoreSubmissionIntegrityException(
                $"beatmap hash mismatch ({submissionBeatmapMd5} != {updatedBeatmapHash})");
    }

    public static void ValidateSubmissionIntegrity(
        ClientDetails? clientDetails,
        string osuVersion,
        string clientHash,
        string uniqueIds,
        ScoreSubmission score,
        string? storyboardMd5,
        string submissionBeatmapMd5,
        string updatedBeatmapHash)
    {
        var uniqueIdHashes = ParseUniqueIdHashes(uniqueIds);
        ValidateClientDetails(clientDetails, osuVersion, clientHash, uniqueIdHashes);
        ValidateScoreChecksum(score, osuVersion, clientHash, storyboardMd5);
        ValidateBeatmapHash(submissionBeatmapMd5, updatedBeatmapHash);
    }

    private static string Md5Hex(string value)
    {
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    }
}