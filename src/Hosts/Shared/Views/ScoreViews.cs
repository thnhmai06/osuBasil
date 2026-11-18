
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Game.Scores;

namespace Basil.Host.Shared.Views;

public sealed record ScoreView(
	int Id,
	int? UserId,
	string? BeatmapHash,
	GameMode Mode,
	string Mods,
	int TotalScore,
	ushort MaxCombo,
	double Accuracy,
	Grade Grade,
	bool Passed,
	bool FullCombo,
	DateTimeOffset Timestamp,
	double? Pp)
{
	public static ScoreView Of(ScoreRecord score)
	{
		var v = score.Value;
		return new ScoreView(score.Id, v.UserId, v.BeatmapHash?.HashValue, v.Mode, v.Mods.ToString(),
			v.LastState.TotalScore,
			v.LastState.MaxCombo, v.Accuracy, v.Grade, v.IsPassed, v.LastState.IsFullCombo, v.Timestamp,
			v.PerformancePoints);
	}
}
