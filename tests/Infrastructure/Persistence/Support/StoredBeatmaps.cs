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
}
