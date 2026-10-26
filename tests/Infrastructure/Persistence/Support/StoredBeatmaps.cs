using Basil.Application.Persistence.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Content;
using Basil.Domain.Mechanics;

namespace Basil.Infrastructure.Persistence.Tests.Support;

/// <summary>Builds the beatmap records the file storage tests work on, with only the parts they name.</summary>
internal static class StoredBeatmaps
{
	/// <summary>Creates a beatmapset.</summary>
	/// <param name="id">The set's id.</param>
	/// <returns>The set, with no assets of its own.</returns>
	public static BeatmapsetRecord Set(int id = 100)
	{
		var now = DateTimeOffset.UnixEpoch;
		return new BeatmapsetRecord
		{
			Id = id,
			Fingerprint = new ContentFingerprint(new ContentStamp(0, now), new Md5("00000000000000000000000000000000")),
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

	/// <summary>Creates a difficulty of a set that <see cref="Set" /> builds.</summary>
	/// <param name="setId">The id of the set the difficulty belongs to.</param>
	/// <returns>The difficulty.</returns>
	public static BeatmapRecord Beatmap(int setId = 100)
	{
		var now = DateTimeOffset.UnixEpoch;
		return new BeatmapRecord
		{
			Id = 321,
			Beatmapset = Set(setId),
			Hash = new Md5("d41d8cd98f00b204e9800998ecf8427e"),
			Metadata = new BeatmapMetadata
			{
				Version = "Primordial Nucleosynthesis",
				Difficulty = new BeatmapDifficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0,
					8.0, 5.0, 6.5),
				Objects = new OsuObjects { MaxCombo = 500 }
			},
			Assets = new BeatmapAssets(),
			UpdatedAt = now,
			CreatedAt = now
		};
	}

	/// <summary>Creates the data that stores the set with one difficulty.</summary>
	/// <param name="setId">The id the set gets: its osu! id.</param>
	/// <returns>The set's data, with the difficulty constructed by <see cref="Beatmap" />.</returns>
	public static BeatmapsetData SetData(int setId)
	{
		var set = Set(setId);
		var beatmap = Beatmap();
		return new BeatmapsetData(setId, set.Fingerprint, set.Metadata, set.Assets,
			[new BeatmapData(321, beatmap.Hash, beatmap.Metadata, beatmap.Assets)]);
	}

	/// <summary>Creates the data of a beatmapset, with the difficulties it has.</summary>
	/// <param name="onlineId">The set's osu! id, or <see langword="null" /> for a new local id.</param>
	/// <param name="artist">The artist of the set's music.</param>
	/// <param name="title">The title of the set's music.</param>
	/// <param name="creator">The name of the set's mapper.</param>
	/// <param name="beatmaps">The difficulties the set has.</param>
	/// <returns>The set's data, as a commit stores it.</returns>
	public static BeatmapsetData Data(int? onlineId, string artist, string title, string creator,
		IReadOnlyList<BeatmapData>? beatmaps = null)
	{
		var now = DateTimeOffset.UnixEpoch;
		return new BeatmapsetData(onlineId,
			new ContentFingerprint(new ContentStamp(0, now), new Md5("00000000000000000000000000000000")),
			new BeatmapsetMetadata { Artist = artist, Title = title, Creator = creator },
			new BeatmapsetAssets(), beatmaps ?? []);
	}

	/// <summary>Creates the data of a difficulty of a beatmapset.</summary>
	/// <param name="onlineId">The difficulty's osu! id, or <see langword="null" /> for a new local id.</param>
	/// <param name="hash">The MD5 of the difficulty's file.</param>
	/// <param name="version">The name of the difficulty.</param>
	/// <param name="mode">The game mode the difficulty is played in.</param>
	/// <param name="cs">The circle size setting.</param>
	/// <param name="ar">The approach rate setting.</param>
	/// <param name="od">The overall difficulty setting.</param>
	/// <param name="hp">The health drain rate setting.</param>
	/// <param name="bpm">The beats per minute of the difficulty.</param>
	/// <param name="lengthSeconds">The length of the difficulty, in seconds.</param>
	/// <param name="circles">The number of circles the difficulty has.</param>
	/// <param name="sliders">The number of sliders the difficulty has.</param>
	/// <returns>The difficulty's data, as a commit stores it.</returns>
	public static BeatmapData Difficulty(int? onlineId, Md5 hash, string version,
		GameMode mode = GameMode.Standard, double cs = 4, double ar = 9, double od = 8, double hp = 5,
		double bpm = 180, int lengthSeconds = 120, int circles = 0, int sliders = 0)
	{
		return new BeatmapData(onlineId, hash, new BeatmapMetadata
		{
			Version = version,
			Difficulty = new BeatmapDifficulty(mode, bpm, TimeSpan.FromSeconds(lengthSeconds), cs, ar, od, hp, 5.0),
			Objects = Objects(mode, circles, sliders)
		}, new BeatmapAssets());
	}

	/// <summary>Gets the hit objects of a difficulty of the given mode.</summary>
	/// <param name="mode">The game mode the difficulty is played in.</param>
	/// <param name="circles">The number of circles the difficulty has.</param>
	/// <param name="sliders">The number of sliders the difficulty has.</param>
	/// <returns>The hit objects of the difficulty.</returns>
	private static BeatmapObjects Objects(GameMode mode, int circles, int sliders)
	{
		return mode == GameMode.Standard
			? new OsuObjects { Circles = circles, Sliders = sliders }
			: BeatmapObjects.Empty(mode);
	}
}