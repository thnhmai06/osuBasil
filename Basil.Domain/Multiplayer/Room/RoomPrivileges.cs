namespace Basil.Domain.Multiplayer.Room;

/// <summary>The roles a user holds in one room; a user can hold several at once.</summary>
[Flags]
public enum RoomPrivileges : uint
{
	None = 0,

	/// <summary>Occupies a slot in the room.</summary>
	Player = 1 << 0,

	/// <summary>Is the room's current host.</summary>
	Host = 1 << 1,

	/// <summary>Is a referee of the room and not its creator.</summary>
	Referee = 1 << 2,

	/// <summary>Created the room.</summary>
	Creator = 1 << 3,

	/// <summary>The roles that moderate the room: referee and creator.</summary>
	Moderators = Referee | Creator,

	/// <summary>The roles that run the match: host, referee and creator.</summary>
	Staff = Host | Moderators
}