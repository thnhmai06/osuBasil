using Basil.Domain.Content;
using Basil.Domain.Game.Mechanics;

namespace Basil.Infrastructure.Beatmaps.Tests.Support;

internal static class AssetBeatmaps
{
	public static BeatmapsetRecord Set(int id = 100)
	{
		var now = DateTimeOffset.UnixEpoch;
		return new BeatmapsetRecord
		{
			Id = id,
			Fingerprint = new ContentFingerprint(new ContentMetadata(0, now), new Md5("00000000000000000000000000000000")),
			Metadata = new BeatmapsetMetadata
			{
				Artist = "Camellia",
				Title = "Exit This Earth's Atomosphere",
				Creator = "ProfessionalBox"
			},
			Assets = new BeatmapsetAssets(),
			UpdatedAt = now,
			CreatedAt = now
		};
	}

	public static BeatmapRecord Beatmap(BeatmapsetRecord set, string? audio = "audio.mp3",
		TimeSpan? previewTime = null)
	{
		var now = DateTimeOffset.UnixEpoch;
		return new BeatmapRecord
		{
			Id = 321,
			Beatmapset = set,
			Hash = new Md5("d41d8cd98f00b204e9800998ecf8427e"),
			Metadata = new BeatmapMetadata
			{
				Version = "Primordial Nucleosynthesis",
				Difficulty = new BeatmapDifficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0,
					8.0, 5.0, 6.5),
				ObjectCount = new OsuObjectCount { MaxCombo = 500 }
			},
			Assets = new BeatmapAssets { AudioFile = audio, PreviewTime = previewTime },
			UpdatedAt = now,
			CreatedAt = now
		};
	}
}