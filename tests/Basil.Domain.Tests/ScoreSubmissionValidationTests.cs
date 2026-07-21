using System.Security.Cryptography;
using System.Text;
using Basil.Domain.Login;
using Basil.Domain.Scores;

namespace Basil.Domain.Tests;

public class ScoreSubmissionValidationTests
{
    [Fact]
    public void ParseUniqueIdHashes_HashesEachHalfIndependently()
    {
        var hashes = ScoreSubmission.ParseUniqueIdHashes("uid1value|uid2value");

        Assert.Equal(Md5("uid1value"), hashes.UniqueId1Md5);
        Assert.Equal(Md5("uid2value"), hashes.UniqueId2Md5);
    }

    private static readonly DateOnly LoginVersionDate = new(2021, 5, 20);

    private static ClientDetails MakeClient()
    {
        return new ClientDetails(
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
        var hashes = ScoreSubmission.ParseUniqueIdHashes("uid1value|uid2value");

        ScoreSubmission.ValidateClientDetails(client, LoginVersionDate, "20210520", client.Hash(), hashes);
    }

    [Fact]
    public void ValidateClientDetails_NullClientDetails_Throws()
    {
        var hashes = ScoreSubmission.ParseUniqueIdHashes("uid1value|uid2value");

        Assert.Throws<ScoreSubmissionIntegrityException>(() =>
            ScoreSubmission.ValidateClientDetails(null, LoginVersionDate, "20210520", "anyhash", hashes));
    }

    [Fact]
    public void ValidateClientDetails_VersionMismatch_Throws()
    {
        var client = MakeClient();
        var hashes = ScoreSubmission.ParseUniqueIdHashes("uid1value|uid2value");

        Assert.Throws<ScoreSubmissionIntegrityException>(() =>
            ScoreSubmission.ValidateClientDetails(client, LoginVersionDate, "20200101", client.Hash(), hashes));
    }

    [Fact]
    public void ValidateClientDetails_UniqueIdMismatch_Throws()
    {
        var client = MakeClient();
        var wrongHashes = ScoreSubmission.ParseUniqueIdHashes("wrong1|wrong2");

        Assert.Throws<ScoreSubmissionIntegrityException>(() =>
            ScoreSubmission.ValidateClientDetails(client, LoginVersionDate, "20210520", client.Hash(), wrongHashes));
    }

    [Fact]
    public void ValidateBeatmapHash_Mismatch_Throws()
    {
        Assert.Throws<ScoreSubmissionIntegrityException>(() =>
            ScoreSubmission.ValidateBeatmapHash("aaa", "bbb"));
    }

    [Fact]
    public void ValidateBeatmapHash_Match_DoesNotThrow()
    {
        ScoreSubmission.ValidateBeatmapHash("same", "same");
    }

    [Fact]
    public void ValidateScoreChecksum_Mismatch_Throws()
    {
        var score = ScoreSubmission.FromSubmission([
            "wrong-checksum", "490", "5", "3", "0", "0", "1", "12345678", "500", "False", "S", "0", "True", "0",
            "210520235959", "20210520 "
        ]) with { BeatmapMd5 = "beatmap_md5_hash_1234567890abcd" };

        Assert.Throws<ScoreSubmissionIntegrityException>(() =>
            score.ValidateScoreChecksum("cookiezi", "20210520", "clienthash", null));
    }

    [Fact]
    public void ValidateScoreChecksum_Match_DoesNotThrow()
    {
        var score = ScoreSubmission.FromSubmission([
            "placeholder", "490", "5", "3", "0", "0", "1", "12345678", "500", "False", "S", "0", "True", "0",
            "210520235959", "20210520 "
        ]) with { BeatmapMd5 = "beatmap_md5_hash_1234567890abcd" };
        score = score with { ClientChecksum = score.ComputeOnlineChecksum("cookiezi", "20210520", "clienthash", "") };

        score.ValidateScoreChecksum("cookiezi", "20210520", "clienthash", null);
    }

    private static string Md5(string value)
    {
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
