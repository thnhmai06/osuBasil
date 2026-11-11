namespace Basil.Domain.Multiplayer;

/// <summary>What a user may do in a room: flat, independent flags, and the named groups a room role holds.</summary>
[Flags]
public enum RoomPrivileges : uint
{
	/// <summary>No privilege.</summary>
	None = 0,

	/// <summary>Move, arrange and set the team of other players.</summary>
	SlotsMovePlayers = 1 << 0,

	/// <summary>Lock or unlock slots.</summary>
	SlotsLock = 1 << 1,

	/// <summary>Change map, mods, freemod, win condition, team type, slot count.</summary>
	SettingsChange = 1 << 2,

	/// <summary>Rename the room.</summary>
	SettingsRename = 1 << 3,

	/// <summary>Set the room password.</summary>
	SettingsPassword = 1 << 4,

	/// <summary>Make the room private.</summary>
	SettingsPrivate = 1 << 5,

	/// <summary>Kick a player from the room.</summary>
	MembersKick = 1 << 6,

	/// <summary>Ban a user from the room.</summary>
	MembersBan = 1 << 7,

	/// <summary>Invite users to the room.</summary>
	MembersInvite = 1 << 9,

	/// <summary>Manage referees.</summary>
	RefereesManage = 1 << 10,

	/// <summary>Transfer host to another player.</summary>
	HostTransfer = 1 << 11,

	/// <summary>Start a round.</summary>
	RoundsStart = 1 << 12,

	/// <summary>Abort a round in progress.</summary>
	RoundsAbort = 1 << 13,

	// 1 << 14 was RoundsCountdown; the countdown moved to BasilBot. The value stays unused and is never reassigned.

	/// <summary>Close the room.</summary>
	RoomClose = 1 << 15,

	/// <summary>Post messages in the room chat.</summary>
	ChatPost = 1 << 16,

	/// <summary>The privileges of a seated player: post in room chat and invite users.</summary>
	Player = ChatPost | MembersInvite,

	/// <summary>The privileges of the current host: the operations the osu! client lets a host do. Hosting grants no management.</summary>
	Host = SettingsChange | SettingsRename | SettingsPassword | SlotsLock | MembersKick | HostTransfer |
	       RoundsStart | RoundsAbort,

	/// <summary>The privileges of a referee: every privilege except managing referees.</summary>
	Referee = Creator & ~RefereesManage,

	/// <summary>The privileges of the room creator, and of a user who manages every room: every room privilege.</summary>
	Creator = SlotsMovePlayers | SlotsLock | SettingsChange | SettingsRename | SettingsPassword | SettingsPrivate |
	          MembersKick | MembersBan | MembersInvite | RefereesManage | HostTransfer | RoundsStart | RoundsAbort |
	          RoomClose | ChatPost
}