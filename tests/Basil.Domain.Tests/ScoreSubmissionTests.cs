using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Domain.Tests;

/// <summary>
///     Fixture fields reused from the Rijndael decryptor oracle fixture (same 16-field submission
///     shape) — see Basil.Infrastructure.Tests' RijndaelScoreDecryptorTests.
/// </summary>
public class ScoreSubmissionTests
{
    private static readonly string[] Fields =
    [
        "abc123checksum", "490", "5", "3", "0", "0", "1", "12345678", "500", "False", "S", "72", "True", "0",
        "210520235959", "20210520 "
    ];

    [Fact]
    public void FromSubmission_ParsesAllFieldsInOrder()
    {
        var score = ScoreSubmission.FromSubmission(Fields);

        Assert.Equal("abc123checksum", score.ClientChecksum);
        Assert.Equal(490, score.HitCounts.x300);
        Assert.Equal(5, score.HitCounts.x100);
        Assert.Equal(3, score.HitCounts.x50);
        Assert.Equal(0, score.HitCounts.xGeki);
        Assert.Equal(0, score.HitCounts.xKatu);
        Assert.Equal(1, score.HitCounts.xMiss);
        Assert.Equal(12345678, score.Score);
        Assert.Equal(500, score.MaxCombo);
        Assert.False(score.IsFullCombo);
        Assert.Equal(Grade.S, score.Grade);
        Assert.Equal(Mods.Hidden | Mods.DoubleTime, score.Mods);
        Assert.True(score.IsPassed);
        Assert.Equal(GameMode.Standard, score.Mode);
        Assert.Equal(new DateTime(2021, 5, 20, 23, 59, 59), score.ClientTime);
        Assert.Equal((ClientFlags)1, score.ClientFlags);
    }

    [Fact]
    public void FromSubmission_RelaxMod_DoesNotAffectMode()
    {
        // GameMode is a plain 4-value enum — Relax/Autopilot are ordinary Mods bits with no effect
        // on the parsed Mode, unlike the old Vanilla/Relax/Autopilot-variant enum.
        var fields = (string[])Fields.Clone();
        fields[11] = ((int)Mods.Relax).ToString(); // mods = RX only
        fields[13] = "0"; // osu!

        var score = ScoreSubmission.FromSubmission(fields);

        Assert.Equal(GameMode.Standard, score.Mode);
        Assert.Equal(Mods.Relax, score.Mods);
    }
}