using Basil.Domain.Scores;

namespace Basil.Domain.Client;

/// <summary>The running score of a spectated player at one moment of a play.</summary>
/// <param name="Time">The time since the start of the play at which the score was sampled.</param>
/// <param name="Id">The id of the frame within the replay.</param>
/// <param name="State">The score at this moment.</param>
/// <param name="Health">The current health value.</param>
/// <param name="TagByte">The tag byte for tag-team mode.</param>
/// <param name="Sv2Portion">The score v2 portions, or <see langword="null" /> when the play does not use score v2.</param>
public readonly record struct ScoreFrame(
	TimeSpan Time,
	int Id,
	ScoreState State,
	int Health,
	int TagByte, //TODO: Can someone reverse-engineer the osu! stable spectating protocol and decode the TagByte bitmask in SPECTATE_FRAMES?
	ScoreV2Portion? Sv2Portion);

/// <summary>The two portions a score v2 total is made of.</summary>
/// <param name="Combo">The part of the score earned from combo.</param>
/// <param name="Bonus">The part of the score earned from bonus.</param>
public readonly record struct ScoreV2Portion(double Combo, double Bonus);