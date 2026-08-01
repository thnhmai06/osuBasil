namespace Basil.Domain.Multiplayer;

/// <summary>
///     Describes the state of a single slot in a multiplayer match.
/// </summary>
/// <remarks>
///     A bitwise combination of flags that track whether a slot is occupied, whether the occupant
///     is ready, and how far the occupant has progressed through a round.
/// </remarks>
[Flags]
public enum SlotStatus : byte
{
	/// <summary>The slot is open and available to join.</summary>
	Open = 1 << 0,
	/// <summary>The slot is locked by the host.</summary>
	Locked = 1 << 1,
	/// <summary>The occupant is in the lobby and not ready.</summary>
	NotReady = 1 << 2,
	/// <summary>The occupant is ready to play.</summary>
	Ready = 1 << 3,
	/// <summary>The occupant has no map selected.</summary>
	NoMap = 1 << 4,
	/// <summary>The occupant is playing the map.</summary>
	Playing = 1 << 5,
	/// <summary>The occupant has finished playing the map.</summary>
	Complete = 1 << 6,
	/// <summary>The occupant has quit the match.</summary>
	Quit = 1 << 7
}
