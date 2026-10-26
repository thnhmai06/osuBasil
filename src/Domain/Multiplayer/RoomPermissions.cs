namespace Basil.Domain.Multiplayer;

/// <summary>The permissions that control what can be done in a room, flat and independent.</summary>
[Flags]
public enum RoomPermissions : uint
{
	/// <summary>No permission.</summary>
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

	/// <summary>Seat or unseat players.</summary>
	MembersSeat = 1 << 8,

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

	/// <summary>Set or cancel countdowns.</summary>
	RoundsCountdown = 1 << 14,

	/// <summary>Close the room.</summary>
	RoomClose = 1 << 15,

	/// <summary>Post messages in the room chat.</summary>
	ChatPost = 1 << 16
}

/// <summary>The permissions granted by each room role, and the union of all permissions.</summary>
/// <remarks>
/// The rights of a role are what the osu! client lets that role do, at least; finer rights only apply to the commands and the API.
/// </remarks>
public static class RoomPermissionsGroups
{
	/// <summary>The union of every room permission.</summary>
	public const RoomPermissions All = RoomPermissions.SlotsMovePlayers | RoomPermissions.SlotsLock |
	                                   RoomPermissions.SettingsChange | RoomPermissions.SettingsRename |
	                                   RoomPermissions.SettingsPassword | RoomPermissions.SettingsPrivate |
	                                   RoomPermissions.MembersKick | RoomPermissions.MembersBan |
	                                   RoomPermissions.MembersSeat | RoomPermissions.MembersInvite |
	                                   RoomPermissions.RefereesManage | RoomPermissions.HostTransfer |
	                                   RoomPermissions.RoundsStart | RoomPermissions.RoundsAbort |
	                                   RoomPermissions.RoundsCountdown | RoomPermissions.RoomClose |
	                                   RoomPermissions.ChatPost;

	/// <summary>Gets the permissions granted by a combination of roles.</summary>
	/// <param name="roles">The roles to combine.</param>
	/// <returns>The union of permissions granted by each role.</returns>
	/// <remarks>Players may post room chat and invite users; the other permissions depend on their room role.</remarks>
	public static RoomPermissions GetPermissions(this RoomRoles roles)
	{
		var permissions = RoomPermissions.None;

		if ((roles & RoomRoles.Player) != 0)
			permissions |= RoomPermissions.ChatPost | RoomPermissions.MembersInvite;

		if ((roles & RoomRoles.Host) != 0)
			permissions |=
				RoomPermissions.SettingsChange |
				RoomPermissions.SettingsRename |
				RoomPermissions.SettingsPassword |
				RoomPermissions.SlotsLock |
				RoomPermissions.MembersKick |
				RoomPermissions.HostTransfer |
				RoomPermissions.RoundsStart |
				RoomPermissions.RoundsAbort |
				RoomPermissions.RoundsCountdown;

		if ((roles & RoomRoles.Referee) != 0)
			permissions |= All & ~RoomPermissions.RefereesManage;

		if ((roles & RoomRoles.Creator) != 0)
			permissions |= All;

		return permissions;
	}
}