using Basil.Domain.Game.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Persistence;

public sealed record ScoreReceipt(ScoreRecord Score, UserStats Stats, ScoreRecord? PreviousBest)
{
	public long TotalScoreBefore => Stats.TotalScore - Score.Value.LastState.TotalScore;

	public long RankedScoreBefore => Stats.RankedScore - (Score.Value.IsPassed ? Score.Value.LastState.TotalScore : 0);
}
