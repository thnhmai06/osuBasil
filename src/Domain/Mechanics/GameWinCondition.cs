namespace Basil.Domain.Mechanics;

/// <summary>
///     Specifies how the winner of a multiplayer match is decided.
/// </summary>
public enum GameWinCondition : byte
{
	/// <summary>The match is decided by the total score of each team.</summary>
	Score = 0,

	/// <summary>The match is decided by the accuracy of each team.</summary>
	Accuracy = 1,

	/// <summary>The match is decided by the combo of each team.</summary>
	Combo = 2,

	/// <summary>The match is decided by the ScoreV2 scoring rules.</summary>
	ScoreV2 = 3
}