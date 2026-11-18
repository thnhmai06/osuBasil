using System.Globalization;
using Basil.Domain.Game.Client;

namespace Basil.Protocol.Game.Stable.Models.Scores;

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
	bool Perfect,
	string Grade,
	int Mods,
	bool IsPassed,
	int Mode,
	DateTimeOffset Timestamp,
	OsuFlags ClientFlags)
{
	private const int FieldCount = 18;

	public static ScoreSubmission Parse(string text)
	{
		var fields = text.Split(':');
		if (fields.Length < FieldCount)
			throw new FormatException("Score submission has too few fields.");

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
			DateTimeOffset.ParseExact(fields[16], "yyMMddHHmmss", CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
			(OsuFlags)(fields[17].Count(c => c == ' ') & ~4));

		int Number(int index)
		{
			return int.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
				? value
				: throw new FormatException($"Score submission field {index} is not a number.");
		}
	}
}