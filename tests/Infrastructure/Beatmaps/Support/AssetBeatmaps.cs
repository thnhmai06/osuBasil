using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Content;
using Basil.Domain.Mechanics;

namespace Basil.Infrastructure.Beatmaps.Tests.Support;

/// <summary>Builds the stored records the asset tests work on, with only the parts they name.</summary>
internal static class AssetBeatmaps
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

	/// <summary>Creates a difficulty of a set.</summary>
	/// <param name="set">The set the difficulty belongs to.</param>
	/// <param name="audio">The name of the audio track the difficulty uses, or <see langword="null" /> for none.</param>
	/// <param name="previewTime">Where its audio preview starts, or <see langword="null" /> when it sets none.</param>
	/// <returns>The difficulty.</returns>
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
				Objects = new OsuObjects { MaxCombo = 500 }
			},
			Assets = new BeatmapAssets { AudioFile = audio, PreviewTime = previewTime },
			UpdatedAt = now,
			CreatedAt = now
		};
	}
}