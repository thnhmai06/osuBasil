using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Content;
using Basil.Domain.Mechanics;

namespace Basil.Domain.Tests;

/// <summary>
///     Verifies the beatmap record's display name.
/// </summary>
public class BeatmapTests
{
	private static BeatmapRecord MakeBeatmap()
	{
		var now = DateTimeOffset.UnixEpoch;
		var beatmapset = new BeatmapsetRecord
		{
			Id = 100,
			Fingerprint = new ContentFingerprint(new ContentStamp(0, now), new Md5("00000000000000000000000000000000")),
			Metadata = new BeatmapsetMetadata
			{
				Artist = "Camellia",
				Title = "Exit This Earth's Atomosphere",
				Creator = "cmyui"
			},
			Assets = new BeatmapsetAssets(),
			UpdatedAt = now,
			CreatedAt = now
		};
		return new BeatmapRecord
		{
			Id = 321,
			Beatmapset = beatmapset,
			Hash = new Md5("d41d8cd98f00b204e9800998ecf8427e"),
			Metadata = new BeatmapMetadata
			{
				Version = "Hyper",
				Difficulty = new BeatmapDifficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0,
					8.0, 5.0, 6.5),
				Objects = new OsuObjects { MaxCombo = 500 }
			},
			Assets = new BeatmapAssets(),
			UpdatedAt = now,
			CreatedAt = now
		};
	}

	[Fact]
	public void Name_FormatsArtistTitleVersion()
	{
		var bmap = MakeBeatmap();

		Assert.Equal("Camellia - Exit This Earth's Atomosphere [Hyper]", bmap.Name);
	}
}