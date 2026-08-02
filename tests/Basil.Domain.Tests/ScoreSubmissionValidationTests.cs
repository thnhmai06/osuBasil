using System.Security.Cryptography;
using System.Text;
using Basil.Domain.Login;
using Basil.Domain.Scores;

namespace Basil.Domain.Tests;

public class ScoreSubmissionValidationTests
{
	private static readonly DateOnly LoginVersionDate = new(2021, 5, 20);

	[Fact]
	public void ParseUniqueIdHashes_HashesEachHalfIndependently()
	{
		var hashes = Submission.ParseUniqueIdHashes("uid1value|uid2value");

		Assert.Equal(Md5("uid1value"), hashes.UniqueId1Md5);
		Assert.Equal(Md5("uid2value"), hashes.UniqueId2Md5);
	}

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
		var hashes = Submission.ParseUniqueIdHashes("uid1value|uid2value");

		Submission.ValidateClientDetails(client, LoginVersionDate, "20210520", client.Hash(), hashes);
	}

	[Fact]
	public void ValidateClientDetails_NullClientDetails_Throws()
	{
		var hashes = Submission.ParseUniqueIdHashes("uid1value|uid2value");

		Assert.Throws<ScoreSubmissionIntegrityException>(() =>
			Submission.ValidateClientDetails(null, LoginVersionDate, "20210520", "anyhash", hashes));
	}

	[Fact]
	public void ValidateClientDetails_VersionMismatch_Throws()
	{
		var client = MakeClient();
		var hashes = Submission.ParseUniqueIdHashes("uid1value|uid2value");

		Assert.Throws<ScoreSubmissionIntegrityException>(() =>
			Submission.ValidateClientDetails(client, LoginVersionDate, "101", client.Hash(), hashes));
	}

	[Fact]
	public void ValidateClientDetails_UniqueIdMismatch_Throws()
	{
		var client = MakeClient();
		var wrongHashes = Submission.ParseUniqueIdHashes("wrong1|wrong2");

		Assert.Throws<ScoreSubmissionIntegrityException>(() =>
			Submission.ValidateClientDetails(client, LoginVersionDate, "20210520", client.Hash(), wrongHashes));
	}

	[Fact]
	public void ValidateBeatmapHash_Mismatch_Throws()
	{
		Assert.Throws<ScoreSubmissionIntegrityException>(() =>
			Submission.ValidateBeatmapHash("aaa", "bbb"));
	}

	[Fact]
	public void ValidateBeatmapHash_Match_DoesNotThrow()
	{
		Submission.ValidateBeatmapHash("same", "same");
	}

	[Fact]
	public void ValidateScoreChecksum_Mismatch_Throws()
	{
		var score = Submission.FromSubmission([
				"wrong-checksum", "490", "5", "3", "0", "0", "1", "12345678", "500", "False", "S", "0", "True", "0",
				"210520235959", "20210520 "
			]) with
			{
				BeatmapMd5 = "beatmap_md5_hash_1234567890abcd"
			};

		Assert.Throws<ScoreSubmissionIntegrityException>(() =>
			score.ValidateScoreChecksum("cookiezi", "20210520", "clienthash", null));
	}

	[Fact]
	public void ValidateScoreChecksum_Match_DoesNotThrow()
	{
		var score = Submission.FromSubmission([
				"placeholder", "490", "5", "3", "0", "0", "1", "12345678", "500", "False", "S", "0", "True", "0",
				"210520235959", "20210520 "
			]) with
			{
				BeatmapMd5 = "beatmap_md5_hash_1234567890abcd"
			};
		score = score with { ClientChecksum = score.ComputeOnlineChecksum("cookiezi", "20210520", "clienthash", "") };

		score.ValidateScoreChecksum("cookiezi", "20210520", "clienthash", null);
	}

	private static string Md5(string value)
	{
		return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
	}
}