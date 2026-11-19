namespace Basil.Domain.Multiplayer.Room;

[Flags]
public enum RoomPrivileges : uint
{
	None = 0,

	SlotsMovePlayers = 1 << 0,

	SlotsLock = 1 << 1,

	SettingsChange = 1 << 2,

	SettingsRename = 1 << 3,

	SettingsPassword = 1 << 4,

	SettingsPrivate = 1 << 5,

	MembersKick = 1 << 6,

	MembersBan = 1 << 7,

	MembersInvite = 1 << 9,

	RefereesManage = 1 << 10,

	HostTransfer = 1 << 11,

	RoundsStart = 1 << 12,

	RoundsAbort = 1 << 13,

	// 1 << 14 was RoundsCountdown; the countdown moved to BasilBot. The value stays unused and is never reassigned.

	RoomClose = 1 << 15,

	ChatPost = 1 << 16,

	Player = ChatPost | MembersInvite,

	Host = SettingsChange | SettingsRename | SettingsPassword | SlotsLock | MembersKick | HostTransfer |
	       RoundsStart | RoundsAbort,

	Referee = Creator & ~RefereesManage,

	Creator = SlotsMovePlayers | SlotsLock | SettingsChange | SettingsRename | SettingsPassword | SettingsPrivate |
	          MembersKick | MembersBan | MembersInvite | RefereesManage | HostTransfer | RoundsStart | RoundsAbort |
	          RoomClose | ChatPost
}