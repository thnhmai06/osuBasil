namespace Basil.Protocol.Multiplayer;

/// <summary>
///     Wire-shape for a live spectator score frame, serialized as the fixed 29-byte scoreframe block
///     plus two doubles when score v2 is active.
/// </summary>
/// <param name="Time">The frame time in milliseconds since the start of the play.</param>
/// <param name="Id">The id of the player the frame belongs to.</param>
/// <param name="Num300">The number of 300 hits so far.</param>
/// <param name="Num100">The number of 100 hits so far.</param>
/// <param name="Num50">The number of 50 hits so far.</param>
/// <param name="NumGeki">The number of geki hits so far.</param>
/// <param name="NumKatu">The number of katu hits so far.</param>
/// <param name="NumMiss">The number of misses so far.</param>
/// <param name="TotalScore">The total score so far.</param>
/// <param name="MaxCombo">The maximum combo reached so far.</param>
/// <param name="CurrentCombo">The current combo.</param>
/// <param name="Perfect"><see langword="true" /> if no misses have occurred; otherwise, <see langword="false" />.</param>
/// <param name="CurrentHp">The current health value.</param>
/// <param name="TagByte">The tag byte for tag-team mode.</param>
/// <param name="ScoreV2"><see langword="true" /> if the play uses score v2; otherwise, <see langword="false" />.</param>
/// <param name="ComboPortion">
///     The combo portion of the score v2 total, or <see langword="null" /> when
///     <see cref="ScoreV2" /> is not set.
/// </param>
/// <param name="BonusPortion">
///     The bonus portion of the score v2 total, or <see langword="null" /> when
///     <see cref="ScoreV2" /> is not set.
/// </param>
public sealed record ScoreFrame(
	int Time,
	int Id,
	int Num300,
	int Num100,
	int Num50,
	int NumGeki,
	int NumKatu,
	int NumMiss,
	int TotalScore,
	int MaxCombo,
	int CurrentCombo,
	bool Perfect,
	int CurrentHp,
	int TagByte,
	bool ScoreV2,
	double? ComboPortion = null,
	double? BonusPortion = null);