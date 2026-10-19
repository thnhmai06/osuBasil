namespace Basil.Domain.Multiplayer;

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