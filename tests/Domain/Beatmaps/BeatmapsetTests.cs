using Basil.Domain.Beatmaps;
using Basil.Domain.Content;
using Basil.Domain.Cryptography;
using Basil.Domain.Shared;

namespace Basil.Domain.Tests.Beatmaps;

public class BeatmapsetTests
{
	private static BeatmapsetRecord Make(int id)
	{
		var now = DateTimeOffset.UnixEpoch;
		return new BeatmapsetRecord
		{
			Id = id,
			Fingerprint = new ContentFingerprint(new ContentStamp(0, now), new Md5("00000000000000000000000000000000")),
			Metadata = new BeatmapsetMetadata { Artist = "Artist", Title = "Title", Creator = "Creator" },
			Assets = new BeatmapsetAssets(),
			UpdatedAt = now,
			CreatedAt = now
		};
	}

	[Fact]
	public void IdBelowFloor_ReturnsFalse()
	{
		Assert.False(Make(999_999_999).IsLocallyIngested);
	}

	[Fact]
	public void IdAtFloor_ReturnsTrue()
	{
		Assert.True(Make(1_000_000_000).IsLocallyIngested);
	}

	[Fact]
	public void IdAboveFloor_ReturnsTrue()
	{
		Assert.True(Make(1_000_000_001).IsLocallyIngested);
	}
}