using System.Globalization;
using Basil.Domain.Mechanics;

namespace Basil.Domain.Scores;

/// <summary>
///     Represents the scoring fields of a play as submitted by the osu! client.
/// </summary>
/// <param name="UserId">The user ID of the player, when known.</param>
/// <param name="BeatmapMd5">The MD5 checksum of the beatmap played, when known.</param>
/// <param name="Mode">The game mode the play used.</param>
/// <param name="Mods">The mods applied to the play.</param>
/// <param name="HitCounts">The judgment counts of the play.</param>
/// <param name="TotalScore">The total score achieved.</param>
/// <param name="MaxCombo">The maximum combo reached.</param>
/// <param name="Grade">The grade earned.</param>
/// <param name="IsPassed">Whether the play was passed.</param>
/// <param name="IsFullCombo">Whether the play was a full combo.</param>
/// <param name="OccuredAt">The date and time when the play occurred.</param>
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
	public int TotalScore { get; init; } = TotalScore >= 0
		? TotalScore
		: throw new ArgumentOutOfRangeException(nameof(TotalScore), "TotalScore must be non-negative.");

	public short MaxCombo { get; init; } = MaxCombo >= 0
		? MaxCombo
		: throw new ArgumentOutOfRangeException(nameof(MaxCombo), "MaxCombo must be non-negative.");

	/// <summary>
	///     Gets the play's accuracy, computed from its hit counts under its mode and mods.
	/// </summary>
	public double Accuracy => HitCounts.CalculateAccuracy(Mode, Mods);

	/// <summary>
	///     Parses the scoring fields of a submission into a <see cref="Score" />.
	/// </summary>
	/// <param name="submitFields">
	///     The colon-delimited submission fields that follow the submission MD5 entry. Indexes 1
	///     through 14 carry the hit counts, total score, max combo, full-combo flag, grade, mods,
	///     passed flag, mode, and occurrence time.
	/// </param>
	/// <param name="beatmapMd5">The beatmap MD5 to carry into the parsed score, if known.</param>
	/// <param name="userId">The user ID to carry into the parsed score, if known.</param>
	/// <returns>The parsed score.</returns>
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