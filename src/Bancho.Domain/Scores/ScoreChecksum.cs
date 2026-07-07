using System.Security.Cryptography;
using System.Text;

namespace Bancho.Domain.Scores;

/// <summary>
/// Ported from app/objects/score.py's Score.compute_online_checksum. The exact format string
/// (and field order, which does not match the format-arg index order — {15} is placed before
/// {14} in the source template) must be preserved byte-for-byte since it's an interop checksum
/// verified against the osu! client's own computation.
/// </summary>
public static class ScoreChecksum
{
    public static string Compute(
        int n100,
        int n300,
        int n50,
        int ngeki,
        int nkatu,
        int nmiss,
        string beatmapMd5,
        int maxCombo,
        bool perfect,
        string playerName,
        long score,
        string gradeName,
        int mods,
        bool passed,
        int modeVanilla,
        DateTime clientTime,
        string osuVersion,
        string osuClientHash,
        string storyboardChecksum)
    {
        var raw =
            $"chickenmcnuggets{n100 + n300}o15{n50}{ngeki}smustard{nkatu}{nmiss}uu{beatmapMd5}{maxCombo}" +
            $"{PyBool(perfect)}{playerName}{score}{gradeName}{mods}Q{PyBool(passed)}{modeVanilla}" +
            $"{osuVersion}{clientTime:yyMMddHHmmss}{osuClientHash}{storyboardChecksum}";

        var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash);
    }

    private static string PyBool(bool value) => value ? "True" : "False";
}
