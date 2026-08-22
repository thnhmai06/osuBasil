using Basil.Application.Configurations;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Scores;
using Basil.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Tests.Storage;

/// <summary>
///     Verifies the `.osr` header <see cref="FileSystemReplayStorage" /> builds around the raw
///     LZMA replay bytes, byte-for-byte against a golden oracle file.
/// </summary>
public class FileSystemReplayStorageTests
{
	[Fact]
	public async Task WriteAsync_BuildsCompleteOsr_MatchesPythonOracle()
	{
		var replaysPath = Path.Combine(Path.GetTempPath(), $"basil-replays-{Guid.NewGuid()}");
		try
		{
			var storage = new FileSystemReplayStorage(
				Options.Create(new StorageOptions
				{
					ReplaysPath = replaysPath,
					AvatarsPath = replaysPath,
					MapsetsPath = replaysPath,
					MenuSeasonalsPath = replaysPath,
					MenuBannersPath = replaysPath,
					FaqsPath = replaysPath,
					CachePath = replaysPath
				}));

			var score = new Submission
			{
				BeatmapMd5 = "c0aabbccddeeff001122334455667788",
				UserId = 1,
				HitCounts = new HitCounts(490, 5, 3, 100, 2, 1),
				Score = 12345678,
				MaxCombo = 500,
				IsFullCombo = false,
				Grade = Grade.S,
				Mods = (Mods)72, // Hidden (8) | DoubleTime (64)
				IsPassed = true,
				Mode = GameMode.Standard,
				ClientTime = new DateTime(2021, 5, 20, 23, 59, 59),
				ServerTime = new DateTime(2021, 5, 20, 23, 59, 59, DateTimeKind.Utc)
			};
			var osuVersion = OsuVersion.From("b20260711.1");
			var replayData = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();

			await storage.WriteAsync(555L, score, "cookiezi", osuVersion, replayData);

			var written = await storage.ReadAsync(555L);
			Assert.NotNull(written);
			Assert.Equal(
				Convert.FromHexString(
					"00672735010B2063306161626263636464656566663030313132323333343435353636373738380B08636F6F6B69657A690B203034323533623031303562373966383737666633646464646666333932653335EA01050003006400020001004E61BC00F40100480000000080E9E85FEB1BD9081E000000000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D2B02000000000000"),
				written);
		}
		finally
		{
			if (Directory.Exists(replaysPath)) Directory.Delete(replaysPath, true);
		}
	}
}