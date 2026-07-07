using Bancho.Domain.Scores;
namespace Bancho.Domain.Tests;

/// <summary>
/// Expected checksum generated from bancho.py's exact format string via a pure-Python oracle
/// script (hashlib.md5 over the literal template), not by re-deriving the C# logic — see
/// docs/csharp-migration-plan.md Phase 6 notes.
/// </summary>
public class ScoreChecksumTests
{
    [Fact]
    public void Compute_MatchesPythonOracle()
    {
        var checksum = ScoreChecksum.Compute(
            n100: 5,
            n300: 490,
            n50: 3,
            ngeki: 100,
            nkatu: 2,
            nmiss: 1,
            beatmapMd5: "beatmap_md5_hash_1234567890abcd",
            maxCombo: 500,
            perfect: false,
            playerName: "cookiezi",
            score: 12345678,
            gradeName: "S",
            mods: 72, // Hidden (8) | DoubleTime (64)
            passed: true,
            modeVanilla: 0,
            clientTime: new DateTime(2021, 5, 20, 23, 59, 59),
            osuVersion: "20210520",
            osuClientHash: "clienthash123",
            storyboardChecksum: "sb_checksum_xyz");

        Assert.Equal("1279de2ae25159f580820f4f78ac86c2", checksum);
    }
}
