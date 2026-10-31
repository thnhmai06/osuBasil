using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Host.Shared.Views;

/// <summary>A score as the API shows it.</summary>
public sealed record ScoreView(
	int Id,
	int? UserId,
	string? BeatmapHash,
	GameMode Mode,
	string Mods,
	int TotalScore,
	short MaxCombo,
	double Accuracy,
	Grade Grade,
	bool Passed,
	bool FullCombo,
	DateTimeOffset Timestamp)
{
	/// <summary>Describes a score.</summary>
	public static ScoreView Of(ScoreRecord score)
	{
		var v = score.Value;
		return new ScoreView(score.Id, v.UserId, v.BeatmapHash?.HashValue, v.Mode, v.Mods.ToString(), v.TotalScore,
			v.MaxCombo, v.Accuracy, v.Grade, v.IsPassed, v.IsFullCombo, v.Timestamp);
	}
}