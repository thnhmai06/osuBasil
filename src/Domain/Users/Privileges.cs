using Perm = Basil.Domain.Users.Permissions;

namespace Basil.Domain.Users;

/// <summary>The roles osu! knows, as the privilege bitmask reported to an osu! client.</summary>
/// <remarks>
///     A bitwise combination of the roles a user is reported in. Privileges are derived from the user's effective
///     <see cref="Permissions" />, are never stored or configured, and grant nothing: a client that claims a role it
///     holds no permission for is refused.
/// </remarks>
[Flags]
public enum Privileges : byte
{
	/// <summary>No role.</summary>
	None = 0,

	/// <summary>A regular player.</summary>
	Player = 1 << 0,

	/// <summary>A moderator.</summary>
	Moderator = 1 << 1,

	/// <summary>A supporter; osu!tourney also requires this.</summary>
	Supporter = 1 << 2,

	/// <summary>The server owner.</summary>
	Owner = 1 << 3,

	/// <summary>A developer.</summary>
	Developer = 1 << 4,

	/// <summary>A tournament staff member.</summary>
	Tournament = 1 << 5
}

/// <summary>The permissions that belong directly to each role.</summary>
public static class PrivilegesExtensions
{
	/// <summary>Determines whether every required role is held.</summary>
	/// <param name="held">The roles held.</param>
	/// <param name="required">The roles that must all be held; none is always satisfied.</param>
	/// <returns><see langword="true" /> if every role in <paramref name="required" /> is held.</returns>
	public static bool Allows(this Privileges held, Privileges required)
	{
		return (held & required) == required;
	}

	/// <param name="role">The roles; a combination is the union of its roles' permissions.</param>
	extension(Privileges role)
	{
		/// <summary>Gets the permissions a role holds directly.</summary>
		/// <returns>The permissions that belong to every role set in <paramref name="role" />, at depth one.</returns>
		public Permissions Permissions => PermissionsOf(role);
	}

	/// <summary>The permissions that belong to the player role.</summary>
	private static readonly Permissions PlayerPermissions = Perm.RoomCreate | Perm.RoomJoin | Perm.ChatPost |
	                                                        Perm.ChatPostPrivate | Perm.SpectatingWatch |
	                                                        Perm.ScoreSubmit | Perm.BeatmapsetUpload |
	                                                        Perm.ScreenshotUpload;

	/// <summary>The permissions that belong to the tournament role, without the player's.</summary>
	private static readonly Permissions TournamentPermissions = (Perm.RoomObserve | Perm.RoomViewPrivate |
	                                                             Perm.RoomManageAny | Perm.RoomCreateUnlimited |
	                                                             Perm.ChatPostAnyRoom | Perm.BeatmapsetManage |
	                                                             Perm.BeatmapsetViewHidden) & ~PlayerPermissions;

	/// <summary>Gets the permissions the roles in a mask hold.</summary>
	private static Permissions PermissionsOf(Privileges role)
	{
		var permissions = Perm.None;
		if ((role & Privileges.Player) != 0) permissions |= PlayerPermissions;
		if ((role & Privileges.Supporter) != 0) permissions |= Perm.BeatmapsetDirect;
		if ((role & Privileges.Tournament) != 0) permissions |= TournamentPermissions;
		if ((role & Privileges.Moderator) != 0)
			permissions |= Perm.UserSilence | Perm.UserRestrict | Perm.UserNote | Perm.UserViewLogins |
			               Perm.ChatMessageAnyone | Perm.ChatAnnounce | Perm.ScreenshotDelete;
		if ((role & Privileges.Developer) != 0)
			permissions |= Perm.ServerConfigure | Perm.ServerViewDiagnostics;
		if ((role & Privileges.Owner) != 0)
			permissions |= Perm.UserManage | Perm.UserViewDeleted | Perm.UserManagePermissions |
			               Perm.ServerManageContent;
		return permissions;
	}
}