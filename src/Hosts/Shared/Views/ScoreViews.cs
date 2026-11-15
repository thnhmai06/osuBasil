using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Host.Shared.Views;

/// <summary>A score as the API shows it.</summary>
/// <remarks><c>Pp</c> is <see langword="null" /> when the score has no calculated pp.</remarks>
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
	/// <summary>Describes a score.</summary>
	/// <param name="score">The score.</param>
	public static ScoreView Of(ScoreRecord score)
	{
		var v = score.Value;
		return new ScoreView(score.Id, v.UserId, v.BeatmapHash?.HashValue, v.Mode, v.Mods.ToString(),
			v.LastState.TotalScore,
			v.LastState.MaxCombo, v.Accuracy, v.Grade, v.IsPassed, v.LastState.IsFullCombo, v.Timestamp,
			v.Pp);
	}
}
