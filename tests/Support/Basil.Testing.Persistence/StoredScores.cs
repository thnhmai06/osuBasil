using System.Security.Cryptography;
using System.Text;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Testing.Persistence;

/// <summary>Builds the score records the persistence tests work on, with only the parts they name.</summary>
public static class StoredScores
{
	/// <summary>Creates the digest of some name, so a test can name the files it plays.</summary>
	/// <param name="name">The name the digest is taken from.</param>
	/// <returns>The digest.</returns>
	public static Md5 Hash(string name)
	{
		return new Md5(MD5.HashData(Encoding.UTF8.GetBytes(name)));
	}

	/// <summary>Creates the data of a submitted play.</summary>
	/// <param name="player">The account that played, or <see langword="null" /> for an unknown player.</param>
	/// <param name="beatmapHash">The hash of the beatmap played, or <see langword="null" /> for an unknown one.</param>
	/// <param name="totalScore">The total score the play achieved.</param>
	/// <param name="timestamp">The moment the play happened.</param>
	/// <param name="checksum">The checksum the client computed for the submission, or <see langword="null" />.</param>
	/// <param name="mods">The mods applied to the play.</param>
	/// <param name="mode">The game mode the play used.</param>
	/// <returns>The play's data, as a commit stores it.</returns>
	public static ScoreData Data(UserRecord? player, Md5? beatmapHash, int totalScore, DateTimeOffset timestamp,
		Md5? checksum = null, GameMods mods = GameMods.NoMod, GameMode mode = GameMode.Standard)
	{
		return new ScoreData(player?.Id, beatmapHash, mode, mods, new HitCounts(100, 0, 0, 0, 0, 0), totalScore,
			100, 50, Grade.A, true, false, timestamp)
		{
			Checksum = checksum
		};
	}
}