namespace Basil.Protocol.Stable.Models.Wire;

/// <summary>The team type numbers the osu! client uses in a multiplayer match.</summary>
public enum WireTeamType : byte
{
	/// <summary>Every player plays alone.</summary>
	HeadToHead = 0,

	/// <summary>All players share one score as a tag team.</summary>
	TagCoop = 1,

	/// <summary>Players are split into a blue and a red team.</summary>
	TeamVs = 2,

	/// <summary>Players are split into blue and red tag teams.</summary>
	TagTeamVs = 3
}