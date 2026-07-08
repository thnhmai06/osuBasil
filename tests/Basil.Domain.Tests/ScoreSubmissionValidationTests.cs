using System.Security.Cryptography;
using System.Text;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Scores;

namespace Basil.Domain.Tests;

public class ScoreSubmissionValidationTests
{
    [Fact]
    public void ParseUniqueIdHashes_HashesEachHalfIndependently()
    {
        var hashes = ScoreSubmissionValidation.ParseUniqueIdHashes("uid1value|uid2value");

        Assert.Equal(Md5("uid1value"), hashes.UniqueId1Md5);
        Assert.Equal(Md5("uid2value"), hashes.UniqueId2Md5);
    }

    private static ClientDetails MakeClient(DateOnly? version = null)
    {
        return new ClientDetails(
            version ?? new DateOnly(2021, 5, 20),
            "pathmd5",
            "adaptersmd5",
            Md5("uid1value"),
            Md5("uid2value"),
            ["eth0"]);
    }

    [Fact]
    public void ValidateClientDetails_AllMatching_DoesNotThrow()
    {
        var client = MakeClient();
        var hashes = ScoreSubmissionValidation.ParseUniqueIdHashes("uid1value|uid2value");

        ScoreSubmissionValidation.ValidateClientDetails(client, "20210520", client.ClientHash, hashes);
    }

    [Fact]
    public void ValidateClientDetails_NullClientDetails_Throws()
    {
        var hashes = ScoreSubmissionValidation.ParseUniqueIdHashes("uid1value|uid2value");

        Assert.Throws<ScoreSubmissionIntegrityException>(() =>
            ScoreSubmissionValidation.ValidateClientDetails(null, "20210520", "anyhash", hashes));
    }

    [Fact]
    public void ValidateClientDetails_VersionMismatch_Throws()
    {
        var client = MakeClient();
        var hashes = ScoreSubmissionValidation.ParseUniqueIdHashes("uid1value|uid2value");

        Assert.Throws<ScoreSubmissionIntegrityException>(() =>
            ScoreSubmissionValidation.ValidateClientDetails(client, "20200101", client.ClientHash, hashes));
    }

    [Fact]
    public void ValidateClientDetails_UniqueIdMismatch_Throws()
    {
        var client = MakeClient();
        var wrongHashes = ScoreSubmissionValidation.ParseUniqueIdHashes("wrong1|wrong2");

        Assert.Throws<ScoreSubmissionIntegrityException>(() =>
            ScoreSubmissionValidation.ValidateClientDetails(client, "20210520", client.ClientHash, wrongHashes));
    }

    [Fact]
    public void ValidateBeatmapHash_Mismatch_Throws()
    {
        Assert.Throws<ScoreSubmissionIntegrityException>(() =>
            ScoreSubmissionValidation.ValidateBeatmapHash("aaa", "bbb"));
    }

    [Fact]
    public void ValidateBeatmapHash_Match_DoesNotThrow()
    {
        ScoreSubmissionValidation.ValidateBeatmapHash("same", "same");
    }

    [Fact]
    public void ValidateScoreChecksum_Mismatch_Throws()
    {
        var bmap = MakeBeatmap();
        var score = ScoreSubmission.FromSubmission([
            "wrong-checksum", "490", "5", "3", "0", "0", "1", "12345678", "500", "False", "S", "0", "True", "0",
            "210520235959", "20210520 "
        ]);
        score.Bmap = bmap;
        score.PlayerName = "cookiezi";

        Assert.Throws<ScoreSubmissionIntegrityException>(() =>
            ScoreSubmissionValidation.ValidateScoreChecksum(score, "20210520", "clienthash", null));
    }

    [Fact]
    public void ValidateScoreChecksum_Match_DoesNotThrow()
    {
        var bmap = MakeBeatmap();
        var score = ScoreSubmission.FromSubmission([
            "placeholder", "490", "5", "3", "0", "0", "1", "12345678", "500", "False", "S", "0", "True", "0",
            "210520235959", "20210520 "
        ]);
        score.Bmap = bmap;
        score.PlayerName = "cookiezi";
        score.ClientChecksum = score.ComputeOnlineChecksum("20210520", "clienthash", "");

        ScoreSubmissionValidation.ValidateScoreChecksum(score, "20210520", "clienthash", null);
    }

    private static Beatmap MakeBeatmap()
    {
        return new Beatmap(
            "beatmap_md5_hash_1234567890abcd",
            1,
            1,
            "a",
            "b",
            "c",
            "d",
            DateTime.UtcNow,
            1,
            500,
            RankedStatus.Ranked,
            false,
            0,
            0,
            GameMode.VanillaOsu,
            1,
            1,
            1,
            1,
            1,
            1,
            "f.osu");
    }

    private static string Md5(string value)
    {
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    }
}