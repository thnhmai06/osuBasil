using Basil.Domain.Users;
using Basil.Domain.Game.Users;

namespace Basil.Host.Cli.Dashboard.Shell;

internal static class UserInk
{
	public static Ink Of(UserPrivileges privileges)
	{
		return privileges.HasFlag(UserPrivileges.Owner) || privileges.HasFlag(UserPrivileges.Developer) ? Ink.RoleStaff
			: privileges.HasFlag(UserPrivileges.Moderator) ? Ink.RoleModerator
			: privileges.HasFlag(UserPrivileges.Supporter) ? Ink.RoleSupporter
			: privileges.HasFlag(UserPrivileges.Player) ? Ink.RolePlayer
			: Ink.RoleRestricted;
	}
}
