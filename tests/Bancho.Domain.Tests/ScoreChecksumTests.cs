using Bancho.Domain.Scores;

namespace Bancho.Domain.Tests;

/// <summary>
///     Expected checksum generated from bancho.py's exact format string via a pure-Python oracle
///     script (hashlib.md5 over the literal template), not by re-deriving the C# logic — see
///     docs/csharp-migration-plan.md Phase 6 notes.
/// </summary>
public class ScoreChecksumTests
{
    [Fact]
    public void Compute_MatchesPythonOracle()
    {
        var checksum = ScoreChecksum.Compute(
            5,
            490,
            3,
            100,
            2,
            1,
            "beatmap_md5_hash_1234567890abcd",
            500,
            false,
            "cookiezi",
            12345678,
            "S",
            72, // Hidden (8) | DoubleTime (64)
            true,
            0,
            new DateTime(2021, 5, 20, 23, 59, 59),
            "20210520",
            "clienthash123",
            "sb_checksum_xyz");

        Assert.Equal("1279de2ae25159f580820f4f78ac86c2", checksum);
    }
}