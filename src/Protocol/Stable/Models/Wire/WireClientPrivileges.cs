namespace Basil.Protocol.Stable.Models.Wire;

/// <summary>The privilege bits the osu! client reads from the privileges packet.</summary>
[Flags]
public enum WireClientPrivileges : uint
{
	/// <summary>No bit.</summary>
	None = 0,

	/// <summary>A normal player.</summary>
	Player = 1 << 0,

	/// <summary>A moderator.</summary>
	Moderator = 1 << 1,

	/// <summary>A supporter.</summary>
	Supporter = 1 << 2,

	/// <summary>An owner.</summary>
	Owner = 1 << 3,

	/// <summary>A developer.</summary>
	Developer = 1 << 4,

	/// <summary>A tournament client.</summary>
	Tournament = 1 << 5
}