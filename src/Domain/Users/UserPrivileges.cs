namespace Basil.Domain.Users;

/// <summary>The roles osu! knows, as the privilege bitmask reported to an osu! client.</summary>
/// <remarks>
///     A bitwise combination of the roles a user is reported in. Privileges are derived from the user's effective
///     <see cref="UserPermissions" />, are never stored or configured, and grant nothing: a client that claims a role it
///     holds no permission for is refused.
/// </remarks>
[Flags]
public enum UserPrivileges : byte
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
	/// <summary>The permissions that belong to the player role.</summary>
	private const UserPermissions PlayerPermissions =
		UserPermissions.RoomCreate | UserPermissions.RoomJoin | UserPermissions.ChatPost |
		UserPermissions.ChatPostPrivate | UserPermissions.SpectatingWatch |
		UserPermissions.ScoreSubmit | UserPermissions.BeatmapsetUpload |
		UserPermissions.ScreenshotUpload;

	/// <summary>The permissions that belong to the tournament role, without the player's.</summary>
	private const UserPermissions TournamentPermissions =
		(UserPermissions.RoomObserve | UserPermissions.RoomViewPrivate |
		 UserPermissions.RoomManageAny | UserPermissions.RoomCreateUnlimited |
		 UserPermissions.ChatPostAnyRoom | UserPermissions.BeatmapsetManage |
		 UserPermissions.BeatmapsetViewHidden) & ~PlayerPermissions;

	/// <param name="role">The roles; a combination is the union of its roles' permissions.</param>
	extension(UserPrivileges role)
	{
		/// <summary>Gets the permissions a role holds directly.</summary>
		/// <returns>The permissions that belong to every role set in <paramref name="role" />, at depth one.</returns>
		public UserPermissions Permissions
		{
			get
			{
				var permissions = UserPermissions.None;
				if ((role & UserPrivileges.Player) != 0) permissions |= PlayerPermissions;
				if ((role & UserPrivileges.Supporter) != 0) permissions |= UserPermissions.BeatmapsetDirect;
				if ((role & UserPrivileges.Tournament) != 0) permissions |= TournamentPermissions;
				if ((role & UserPrivileges.Moderator) != 0)
					permissions |= UserPermissions.UserSilence | UserPermissions.UserRestrict |
					               UserPermissions.UserNote |
					               UserPermissions.UserViewLogins |
					               UserPermissions.ChatMessageAnyone | UserPermissions.ChatAnnounce |
					               UserPermissions.ScreenshotDelete;
				if ((role & UserPrivileges.Developer) != 0)
					permissions |= UserPermissions.ServerConfigure | UserPermissions.ServerViewDiagnostics;
				if ((role & UserPrivileges.Owner) != 0)
					permissions |= UserPermissions.UserManage | UserPermissions.UserViewDeleted |
					               UserPermissions.UserManagePermissions |
					               UserPermissions.ServerManageContent;
				return permissions;
			}
		}

		/// <summary>Determines whether every required role is held.</summary>
		/// <param name="required">The roles that must all be held; none is always satisfied.</param>
		/// <returns><see langword="true" /> if every role in <paramref name="required" /> is held.</returns>
		public bool Allows(UserPrivileges required)
		{
			return (role & required) == required;
		}
	}
}