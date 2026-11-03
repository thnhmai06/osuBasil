namespace Basil.Protocol.Stable.Models.Wire;

/// <summary>The slot team numbers the osu! client uses in a multiplayer match.</summary>
public enum WireSlotTeam : byte
{
	/// <summary>The slot is on no team.</summary>
	Neutral = 0,

	/// <summary>The slot is on the blue team.</summary>
	Blue = 1,

	/// <summary>The slot is on the red team.</summary>
	Red = 2
}