namespace Basil.Protocol.Stable.Models.Wire;

/// <summary>The slot status numbers the osu! client uses in a multiplayer match.</summary>
public enum WireSlotStatus : byte
{
	/// <summary>The slot is open.</summary>
	Open = 1,

	/// <summary>The slot is locked.</summary>
	Locked = 2,

	/// <summary>The player is not ready.</summary>
	NotReady = 4,

	/// <summary>The player is ready.</summary>
	Ready = 8,

	/// <summary>The player does not have the beatmap.</summary>
	NoMap = 16,

	/// <summary>The player is playing.</summary>
	Playing = 32,

	/// <summary>The player has finished playing.</summary>
	Complete = 64,

	/// <summary>The player has left the slot.</summary>
	Quit = 128
}