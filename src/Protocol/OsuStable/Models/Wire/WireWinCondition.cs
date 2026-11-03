namespace Basil.Protocol.OsuStable.Models.Wire;

/// <summary>The win condition numbers the osu! client uses in a multiplayer match.</summary>
public enum WireWinCondition : byte
{
	/// <summary>The highest score wins.</summary>
	Score = 0,

	/// <summary>The highest accuracy wins.</summary>
	Accuracy = 1,

	/// <summary>The highest combo wins.</summary>
	Combo = 2,

	/// <summary>The highest score wins, with the ScoreV2 rules.</summary>
	ScoreV2 = 3
}
