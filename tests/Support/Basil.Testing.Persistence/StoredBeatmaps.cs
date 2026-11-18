using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Content;
using Basil.Domain.Game.Mechanics;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Testing.Persistence;

public static class StoredBeatmaps
{
	public static BeatmapsetRecord Set(int id = 100)
	{
		var now = DateTimeOffset.UnixEpoch;
		return new BeatmapsetRecord
		{
			Id = id,
			Content = new ContentIdentifier(new ContentMetadata(0, now), new Md5Hash("00000000000000000000000000000000")),
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

	public static BeatmapRecord Beatmap(int setId = 100)
	{
		var now = DateTimeOffset.UnixEpoch;
		return new BeatmapRecord
		{
			Id = 321,
			Beatmapset = Set(setId),
			Hash = new Md5Hash("d41d8cd98f00b204e9800998ecf8427e"),
			Metadata = new BeatmapMetadata
			{
				Version = "Primordial Nucleosynthesis",
				Difficulty = new BeatmapDifficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0,
					8.0, 5.0, 6.5),
				ObjectCount = new OsuObjectCount { MaxCombo = 500 }
			},
			Assets = new BeatmapAssets(),
			UpdatedAt = now,
			CreatedAt = now
		};
	}

	public static BeatmapsetData SetData(int setId)
	{
		var set = Set(setId);
		var beatmap = Beatmap();
		return new BeatmapsetData(setId, set.Content, set.Metadata, set.Assets,
			[new BeatmapData(321, beatmap.Hash, beatmap.Metadata, beatmap.Assets)]);
	}

	public static BeatmapsetData Data(int? onlineId, string artist, string title, string creator,
		IReadOnlyList<BeatmapData>? beatmaps = null)
	{
		var now = DateTimeOffset.UnixEpoch;
		return new BeatmapsetData(onlineId,
			new ContentIdentifier(new ContentMetadata(0, now), new Md5Hash("00000000000000000000000000000000")),
			new BeatmapsetMetadata { Artist = artist, Title = title, Creator = creator },
			new BeatmapsetAssets(), beatmaps ?? []);
	}

	public static BeatmapData Difficulty(int? onlineId, Md5Hash hash, string version,
		GameMode mode = GameMode.Standard, double cs = 4, double ar = 9, double od = 8, double hp = 5,
		double bpm = 180, int lengthSeconds = 120, int circles = 0, int sliders = 0, double stars = 5.0)
	{
		return new BeatmapData(onlineId, hash, new BeatmapMetadata
		{
			Version = version,
			Difficulty = new BeatmapDifficulty(mode, bpm, TimeSpan.FromSeconds(lengthSeconds), cs, ar, od, hp, stars),
			ObjectCount = Objects(mode, circles, sliders)
		}, new BeatmapAssets());
	}

	private static BeatmapObjectCount Objects(GameMode mode, int circles, int sliders)
	{
		return mode == GameMode.Standard
			? new OsuObjectCount { Circles = circles, Sliders = sliders }
			: BeatmapObjectCount.Empty(mode);
	}
}