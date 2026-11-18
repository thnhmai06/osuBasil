namespace Basil.Domain.Game.Scores;

public readonly record struct ScoreState(
	HitCounts HitCounts,
	int TotalScore,
	ushort MaxCombo,
	ushort CurrentCombo,
	bool IsFullCombo)
{
	public readonly int TotalScore = TotalScore >= 0
		? TotalScore
		: throw new ArgumentOutOfRangeException(nameof(TotalScore), TotalScore,
			"The total score must not be negative.");
}