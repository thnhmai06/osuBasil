namespace Basil.Domain.Scores;

/// <summary>The standing of a play at one moment.</summary>
/// <param name="HitCounts">The hits earned so far.</param>
/// <param name="TotalScore">The score so far; not negative.</param>
/// <param name="MaxCombo">The highest combo reached so far.</param>
/// <param name="CurrentCombo">The combo running now.</param>
/// <param name="IsFullCombo">True when no combo has been broken so far.</param>
/// <exception cref="ArgumentOutOfRangeException"><paramref name="TotalScore"/> is negative.</exception>
public readonly record struct ScoreState(
	HitCounts HitCounts,
	int TotalScore,
	ushort MaxCombo,
	ushort CurrentCombo,
	bool IsFullCombo)
{
	/// <summary>The score so far.</summary>
	public readonly int TotalScore = TotalScore >= 0
		? TotalScore
		: throw new ArgumentOutOfRangeException(nameof(TotalScore), TotalScore,
			"The total score must not be negative.");
}