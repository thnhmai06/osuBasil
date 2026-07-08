using System.Globalization;
using Basil.Domain.Beatmaps;

namespace Basil.Domain.Scores;

/// <summary>
///     Ported from app/objects/score.py's Score. Basil's no-pp scope means `pp`/`sr` fields are
///     dropped entirely — every mode ranks by <see cref="Score" /> (the raw score value) instead.
///     PlayerId/PlayerName are plain fields rather than a live session/player reference, since Domain
///     cannot depend on Basil.Application's PlayerSession; the orchestrating use case supplies them.
/// </summary>
public sealed class ScoreSubmission
{
    public long? Id { get; set; }
    public Beatmap? Bmap { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = "";

    public GameMode Mode { get; set; }
    public Mods Mods { get; set; }

    public long Score { get; set; }
    public int MaxCombo { get; set; }
    public double Acc { get; set; }

    public int N300 { get; set; }
    public int N100 { get; set; } // n150 for taiko
    public int N50 { get; set; }
    public int NMiss { get; set; }
    public int NGeki { get; set; }
    public int NKatu { get; set; }

    public Grade Grade { get; set; }

    public bool Passed { get; set; }
    public bool Perfect { get; set; }
    public SubmissionStatus Status { get; set; }

    public DateTime ClientTime { get; set; }
    public DateTime ServerTime { get; set; }
    public int TimeElapsed { get; set; }

    public ClientFlags ClientFlags { get; set; }
    public string ClientChecksum { get; set; } = "";

    public int? Rank { get; set; }
    public ScoreSubmission? PrevBest { get; set; }

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
            ClientChecksum = fields[0],
            N300 = int.Parse(fields[1], CultureInfo.InvariantCulture),
            N100 = int.Parse(fields[2], CultureInfo.InvariantCulture),
            N50 = int.Parse(fields[3], CultureInfo.InvariantCulture),
            NGeki = int.Parse(fields[4], CultureInfo.InvariantCulture),
            NKatu = int.Parse(fields[5], CultureInfo.InvariantCulture),
            NMiss = int.Parse(fields[6], CultureInfo.InvariantCulture),
            Score = long.Parse(fields[7], CultureInfo.InvariantCulture),
            MaxCombo = int.Parse(fields[8], CultureInfo.InvariantCulture),
            Perfect = fields[9] == "True",
            Grade = GradeExtensions.Parse(fields[10]),
            Mods = mods,
            Passed = fields[12] == "True",
            Mode = GameModeExtensions.FromParams(int.Parse(fields[13], CultureInfo.InvariantCulture), mods),
            ClientTime = DateTime.ParseExact(fields[14], "yyMMddHHmmss", CultureInfo.InvariantCulture),
            ClientFlags = (ClientFlags)(fields[15].Count(c => c == ' ') & ~4),
            ServerTime = DateTime.UtcNow
        };
    }

    /// <summary>Ported from Score.calculate_accuracy, applied to and stored on this instance.</summary>
    public double CalculateAccuracy()
    {
        return ScoreAccuracyCalculator.Calculate(Mode.AsVanilla(), N300, N100, N50, NGeki, NKatu, NMiss, Mods);
    }

    /// <summary>Ported from Score.compute_online_checksum.</summary>
    public string ComputeOnlineChecksum(string osuVersion, string osuClientHash, string storyboardChecksum)
    {
        if (Bmap is null)
            throw new InvalidOperationException("Cannot compute an online checksum without a resolved beatmap.");

        return ScoreChecksum.Compute(
            N100, N300, N50, NGeki, NKatu, NMiss, Bmap.Md5, MaxCombo, Perfect, PlayerName, Score,
            Grade.ToString(), (int)Mods, Passed, Mode.AsVanilla(), ClientTime, osuVersion, osuClientHash,
            storyboardChecksum);
    }
}