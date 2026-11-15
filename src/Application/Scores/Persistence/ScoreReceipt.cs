using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Persistence;

/// <summary>What the submitting player is told about a stored score: the score, its effect on the player's totals and the play it beat.</summary>
/// <param name="Score">The stored score; its performance points are not calculated yet.</param>
/// <param name="Stats">The player's statistics in the score's mode including the score.</param>
/// <param name="PreviousBest">
///     The player's best passed score on the beatmap in the same mode before this one, or <see langword="null" />
///     when there was none.
/// </param>
public sealed record ScoreReceipt(ScoreRecord Score, UserStats Stats, ScoreRecord? PreviousBest)
{
	/// <summary>Gets the player's total score before the score.</summary>
	public long TotalScoreBefore => Stats.TotalScore - Score.Value.LastState.TotalScore;

	/// <summary>Gets the player's ranked score before the score; only a passed score adds to it.</summary>
	public long RankedScoreBefore => Stats.RankedScore - (Score.Value.IsPassed ? Score.Value.LastState.TotalScore : 0);
}
