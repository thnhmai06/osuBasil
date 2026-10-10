namespace Basil.Protocol.Bancho.Models.Replay;

/// <summary>
///     Wire-shape for a live spectator score frame, serialized as the fixed 29-byte scoreframe block
///     plus two doubles when score v2 is active.
/// </summary>
public struct ScoreFrame
{
	/// <summary>The frame time in milliseconds since the start of the play.</summary>
	public required int Time { get; init; }

	/// <summary>The id of the player the frame belongs to.</summary>
	public required int Id { get; set; }

	/// <summary>The number of 300 hits so far.</summary>
	public required int Num300 { get; init; }

	/// <summary>The number of 100 hits so far.</summary>
	public required int Num100 { get; init; }

	/// <summary>The number of 50 hits so far.</summary>
	public required int Num50 { get; init; }

	/// <summary>The number of geki hits so far.</summary>
	public required int NumGeki { get; init; }

	/// <summary>The number of katu hits so far.</summary>
	public required int NumKatu { get; init; }

	/// <summary>The number of misses so far.</summary>
	public required int NumMiss { get; init; }

	/// <summary>The total score so far.</summary>
	public required int TotalScore { get; init; }

	/// <summary>The maximum combo reached so far.</summary>
	public required int MaxCombo { get; init; }

	/// <summary>The current combo.</summary>
	public required int CurrentCombo { get; init; }

	/// <summary><see langword="true" /> if no misses have occurred; otherwise, <see langword="false" />.</summary>
	public required bool Perfect { get; init; }

	/// <summary>The current health value.</summary>
	public required int CurrentHp { get; init; }

	/// <summary>The tag byte for tag-team mode.</summary>
	public required int TagByte { get; init; }

	/// <summary><see langword="true" /> if the play uses score v2; otherwise, <see langword="false" />.</summary>
	public required bool ScoreV2 { get; init; }

	/// <summary>
	///     The combo portion of the score v2 total, or <see langword="null" /> when
	///     <see cref="ScoreV2" /> is not set.
	/// </summary>
	public required double? ComboPortion { get; init; }

	/// <summary>
	///     The bonus portion of the score v2 total, or <see langword="null" /> when
	///     <see cref="ScoreV2" /> is not set.
	/// </summary>
	public required double? BonusPortion { get; init; }
}