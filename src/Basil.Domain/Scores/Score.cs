using System.Globalization;
using Basil.Domain.Mechanics;

namespace Basil.Domain.Scores;

public sealed record Score(
	int? UserId,
	string? BeatmapMd5,
	GameMode Mode,
	GameMods Mods,
	HitCounts HitCounts,
	int TotalScore,
	short MaxCombo,
	Grade Grade,
	bool IsPassed,
	bool IsFullCombo,
	DateTimeOffset OccuredAt)
{
	public double Accuracy => HitCounts.CalculateAccuracy(Mode, Mods);

	public static Score Parse(IReadOnlyList<string> submitFields, string? beatmapMd5 = null, int? userId = null)
	{
		var hitCounts = new HitCounts(
			int.Parse(submitFields[1], CultureInfo.InvariantCulture),
			int.Parse(submitFields[2], CultureInfo.InvariantCulture),
			int.Parse(submitFields[3], CultureInfo.InvariantCulture),
			int.Parse(submitFields[4], CultureInfo.InvariantCulture),
			int.Parse(submitFields[5], CultureInfo.InvariantCulture),
			int.Parse(submitFields[6], CultureInfo.InvariantCulture));

		return new Score
		(
			userId,
			beatmapMd5,
			(GameMode)int.Parse(submitFields[13], CultureInfo.InvariantCulture),
			(GameMods)int.Parse(submitFields[11], CultureInfo.InvariantCulture),
			hitCounts,
			int.Parse(submitFields[7], CultureInfo.InvariantCulture),
			short.Parse(submitFields[8], CultureInfo.InvariantCulture), Enum.Parse<Grade>(submitFields[10], true),
			submitFields[12] == "True",
			submitFields[9] == "True",
			DateTime.ParseExact(submitFields[14], "yyMMddHHmmss", CultureInfo.InvariantCulture)
		);
	}
}