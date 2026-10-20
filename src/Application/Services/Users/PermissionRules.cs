using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Users;
using Basil.Domain.Permissions;

namespace Basil.Application.Services.Users;

/// <summary>The permission rules every service applies to the user behind an action.</summary>
internal static class PermissionRules
{
	/// <summary>Gets the effective permissions of an online user at a moment.</summary>
	/// <param name="user">The online user whose restrictions are evaluated.</param>
	/// <param name="now">The moment to evaluate at.</param>
	/// <returns>The permissions granted to the user that no active restriction suspends.</returns>
	internal static Permissions Effective(User user, DateTimeOffset now)
	{
		return user.EffectivePermissions(now);
	}

	/// <summary>Gets the permissions granted to the user behind an open connection.</summary>
	/// <param name="by">The connection acting.</param>
	/// <returns>The permissions granted to the user.</returns>
	internal static Permissions Granted(Connection by)
	{
		return by.User.Record.Value.Permissions;
	}

	/// <summary>Checks whether the user behind a connection may do something now.</summary>
	/// <param name="by">The connection acting.</param>
	/// <param name="required">The permissions the action needs.</param>
	/// <param name="now">The moment to evaluate at.</param>
	/// <returns>Whether the permissions are in effect, not granted, or granted but suspended by a restriction.</returns>
	internal static Access Check(Connection by, Permissions required, DateTimeOffset now)
	{
		return by.User.Check(required, now);
	}

	/// <summary>Gets a value that indicates whether the user behind a connection may do something now.</summary>
	/// <param name="by">The connection acting.</param>
	/// <param name="required">The permissions the action needs.</param>
	/// <param name="now">The moment to evaluate at.</param>
	/// <returns><see langword="true" /> if every required permission is in effect.</returns>
	internal static bool Allows(Connection by, Permissions required, DateTimeOffset now)
	{
		return Check(by, required, now) is Access.Allowed;
	}
}