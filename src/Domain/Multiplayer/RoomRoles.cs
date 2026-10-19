namespace Basil.Domain.Multiplayer;

/// <summary>The roles a connection can hold in a room, combinable as flags.</summary>
[Flags]
public enum RoomRoles : byte
{
	/// <summary>No role.</summary>
	None = 0,

	/// <summary>A seated player.</summary>
	Player = 1,

	/// <summary>The current host.</summary>
	Host = 2,

	/// <summary>A referee.</summary>
	Referee = 4,

	/// <summary>The room creator.</summary>
	Creator = 8
}
