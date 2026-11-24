using Basil.Domain.Scores;

namespace Basil.Domain.Spectating;

/// <summary>A snapshot of the standing of a play while it is being watched.</summary>
/// <param name="Time">The time since the start of the song.</param>
/// <param name="Id">The index of the last hit object judged.</param>
/// <param name="State">The hit counts, score and combo at this moment.</param>
/// <param name="Health">The health bar of the player, as the client reports it; its range is not checked.</param>
/// <param name="TagByte">The game's tag-mode flags for this frame; not decoded.</param>
/// <param name="Sv2Portion">The ScoreV2 split of the score; null when the play is not scored with ScoreV2.</param>
public readonly record struct ScoreFrame(
	TimeSpan Time,
	int Id,
	ScoreState State,
	int Health,
	int TagByte,
	ScoreV2Portion? Sv2Portion);

/// <summary>The parts of a ScoreV2 score.</summary>
/// <param name="Combo">The portion earned from combo.</param>
/// <param name="Bonus">The portion earned from bonus.</param>
public readonly record struct ScoreV2Portion(double Combo, double Bonus);