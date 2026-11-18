using System.Security.Cryptography;
using System.Text;
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Game.Scores;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Testing.Persistence;

public static class StoredScores
{
	public static Md5Hash Hash(string name)
	{
		return new Md5Hash(MD5.HashData(Encoding.UTF8.GetBytes(name)));
	}

	public static ScoreData Data(UserRecord? player, Md5Hash? beatmapHash, int totalScore, DateTimeOffset timestamp,
		Md5Hash? checksum = null, GameMods mods = GameMods.NoMod, GameMode mode = GameMode.Standard)
	{
		return new ScoreData(player?.Id, beatmapHash, mode, mods,
			new ScoreState(new HitCounts(100, 0, 0, 0, 0, 0), totalScore, 100, 50, false), Grade.A, true, timestamp)
		{
			Checksum = checksum
		};
	}
}