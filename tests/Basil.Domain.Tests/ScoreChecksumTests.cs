using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Domain.Tests;

/// <summary>
///     Expected checksum generated from bancho.py's exact format string via a pure-Python oracle
///     script (hashlib.md5 over the literal template), not by re-deriving the C# logic.
/// </summary>
public class ScoreChecksumTests
{
    [Fact]
    public void ComputeOnlineChecksum_MatchesPythonOracle()
    {
        var hitCounts = new HitCounts(x300: 490, x100: 5, x50: 3, xGeki: 100, xKatu: 2, xMiss: 1);
        var score = new ScoreSubmission
        {
            HitCounts = hitCounts,
            BeatmapMd5 = "beatmap_md5_hash_1234567890abcd",
            UserId = 1,
            MaxCombo = 500,
            IsFullCombo = false,
            Score = 12345678,
            Grade = Grade.S,
            Mods = (Mods)72, // Hidden (8) | DoubleTime (64)
            IsPassed = true,
            Mode = GameMode.Standard,
            ClientTime = new DateTime(2021, 5, 20, 23, 59, 59)
        };

        var checksum = score.ComputeOnlineChecksum("cookiezi", "20210520", "clienthash123", "sb_checksum_xyz");

        Assert.Equal("1279de2ae25159f580820f4f78ac86c2", checksum);
    }
}
