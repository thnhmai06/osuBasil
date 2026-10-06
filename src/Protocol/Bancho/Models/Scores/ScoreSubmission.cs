using System.Globalization;

namespace Basil.Protocol.Bancho.Models.Scores;

/// <summary>The decrypted score string the osu! client posts to <c>osu-submit-modular</c>.</summary>
/// <param name="BeatmapHash">The MD5 of the beatmap the client claims to have played.</param>
/// <param name="Username">The player's name as the client sent it.</param>
/// <param name="Checksum">The checksum the client computed for the submission.</param>
/// <param name="Num300">The number of 300 hits.</param>
/// <param name="Num100">The number of 100 hits.</param>
/// <param name="Num50">The number of 50 hits.</param>
/// <param name="NumGeki">The number of geki hits.</param>
/// <param name="NumKatu">The number of katu hits.</param>
/// <param name="NumMiss">The number of misses.</param>
/// <param name="TotalScore">The total score.</param>
/// <param name="MaxCombo">The maximum combo.</param>
/// <param name="IsFullCombo">Whether the client flagged the play as a full combo.</param>
/// <param name="Grade">The grade name as sent.</param>
/// <param name="Mods">The mods bitmask.</param>
/// <param name="IsPassed">Whether the client flagged the play as passed.</param>
/// <param name="Mode">The game mode number.</param>
/// <param name="Timestamp">The time the play was made.</param>
/// <param name="ClientFlags">The anticheat flag bits the client encoded in its version field.</param>
public sealed record ScoreSubmission(
	string BeatmapHash,
	string Username,
	string Checksum,
	int Num300,
	int Num100,
	int Num50,
	int NumGeki,
	int NumKatu,
	int NumMiss,
	int TotalScore,
	int MaxCombo,
	bool IsFullCombo,
	string Grade,
	int Mods,
	bool IsPassed,
	int Mode,
	DateTime Timestamp,
	int ClientFlags)
{
	private const int FieldCount = 18;

	/// <summary>Reads a score submission from its colon-delimited wire text.</summary>
	/// <param name="text">
	///     The fields in order: beatmap MD5, username, checksum, 300, 100, 50, geki, katu, miss, total score, max
	///     combo, full combo, grade, mods, passed, mode, time as <c>yyMMddHHmmss</c>, and the client version field
	///     whose space count (without bit 4) is the client flags.
	/// </param>
	/// <returns>The score submission.</returns>
	/// <exception cref="FormatException"><paramref name="text" /> has too few fields or a malformed field.</exception>
	public static ScoreSubmission Parse(string text)
	{
		var fields = text.Split(':');
		if (fields.Length < FieldCount)
			throw new FormatException("Score submission has too few fields.");

		int Number(int index)
		{
			return int.Parse(fields[index], CultureInfo.InvariantCulture);
		}

		return new ScoreSubmission(
			fields[0],
			fields[1],
			fields[2],
			Number(3),
			Number(4),
			Number(5),
			Number(6),
			Number(7),
			Number(8),
			Number(9),
			Number(10),
			fields[11] == "True",
			fields[12],
			Number(13),
			fields[14] == "True",
			Number(15),
			DateTime.ParseExact(fields[16], "yyMMddHHmmss", CultureInfo.InvariantCulture),
			fields[17].Count(c => c == ' ') & ~4);
	}
}
