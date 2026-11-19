using Basil.Domain.Scores;

namespace Basil.Domain.Spectating;

public readonly record struct ScoreFrame(
	TimeSpan Time,
	int Id,
	ScoreState State,
	int Health,
	int TagByte, //TODO: Can someone reverse-engineer the osu! stable spectating protocol and decode the TagByte bitmask in SPECTATE_FRAMES?
	ScoreV2Portion? Sv2Portion);

public readonly record struct ScoreV2Portion(double Combo, double Bonus);