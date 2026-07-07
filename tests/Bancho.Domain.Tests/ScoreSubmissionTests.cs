using Bancho.Domain.Beatmaps;
using Bancho.Domain.Scores;
namespace Bancho.Domain.Tests;

/// <summary>
/// Fixture fields reused from the Rijndael decryptor oracle fixture (same 16-field submission
/// shape) — see Bancho.Infrastructure.Tests' RijndaelScoreDecryptorTests.
/// </summary>
public class ScoreSubmissionTests
{
    private static readonly string[] Fields =
    [
        "abc123checksum", "490", "5", "3", "0", "0", "1", "12345678", "500", "False", "S", "72", "True", "0",
        "210520235959", "20210520 ",
    ];

    [Fact]
    public void FromSubmission_ParsesAllFieldsInOrder()
    {
        var score = ScoreSubmission.FromSubmission(Fields);

        Assert.Equal("abc123checksum", score.ClientChecksum);
        Assert.Equal(490, score.N300);
        Assert.Equal(5, score.N100);
        Assert.Equal(3, score.N50);
        Assert.Equal(0, score.NGeki);
        Assert.Equal(0, score.NKatu);
        Assert.Equal(1, score.NMiss);
        Assert.Equal(12345678, score.Score);
        Assert.Equal(500, score.MaxCombo);
        Assert.False(score.Perfect);
        Assert.Equal(Grade.S, score.Grade);
        Assert.Equal(Mods.Hidden | Mods.DoubleTime, score.Mods);
        Assert.True(score.Passed);
        Assert.Equal(GameMode.VanillaOsu, score.Mode);
        Assert.Equal(new DateTime(2021, 5, 20, 23, 59, 59), score.ClientTime);
        Assert.Equal((ClientFlags)1, score.ClientFlags);
    }

    [Fact]
    public void FromSubmission_RelaxMod_ShiftsModeToRelaxEquivalent()
    {
        var fields = (string[])Fields.Clone();
        fields[11] = ((int)Mods.Relax).ToString(); // mods = RX only
        fields[13] = "0"; // vanilla osu

        var score = ScoreSubmission.FromSubmission(fields);

        Assert.Equal(GameMode.RelaxOsu, score.Mode);
    }
}
