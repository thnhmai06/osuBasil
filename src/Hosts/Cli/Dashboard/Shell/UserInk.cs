using Basil.Domain.Users;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>Tells how the name of an account is coloured, after the osu! chat console.</summary>
internal static class UserInk
{
	/// <summary>Gets the kind of text for the name of an account, by its highest privilege.</summary>
	/// <param name="privileges">What the account may do.</param>
	/// <returns>Cyan for an owner or developer, red for a moderator, yellow for a supporter, light yellow for a player, grey for a restricted account.</returns>
	public static Ink Of(UserPrivileges privileges)
	{
		return privileges.HasFlag(UserPrivileges.Owner) || privileges.HasFlag(UserPrivileges.Developer) ? Ink.RoleStaff
			: privileges.HasFlag(UserPrivileges.Moderator) ? Ink.RoleModerator
			: privileges.HasFlag(UserPrivileges.Supporter) ? Ink.RoleSupporter
			: privileges.HasFlag(UserPrivileges.Player) ? Ink.RolePlayer
			: Ink.RoleRestricted;
	}
}
