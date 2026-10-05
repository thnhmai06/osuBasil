using Basil.Application.Storage.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Users;

/// <summary>The permission rules every service applies to the user behind an action.</summary>
internal static class PermissionRules
{
	/// <summary>Gets the effective permissions of an online user at a moment.</summary>
	/// <param name="session">The user's session, which holds their running restrictions.</param>
	/// <param name="now">The moment to evaluate at.</param>
	/// <returns>The permissions granted to the user that no active restriction suspends.</returns>
	internal static Permissions Effective(UserSession session, DateTimeOffset now)
	{
		return session.User.Value.Permissions.Effective(session.Restrictions, now);
	}

	/// <summary>Gets the permissions granted to the user behind an open connection.</summary>
	/// <param name="by">The connection acting.</param>
	/// <returns>The granted permissions as the online session knows them, which every change keeps current.</returns>
	internal static Permissions Granted(Connection by)
	{
		return by.Session.User.Value.Permissions;
	}

	/// <summary>Checks whether the user behind a connection may do something now.</summary>
	/// <param name="by">The connection acting.</param>
	/// <param name="required">The permissions the action needs.</param>
	/// <param name="now">The moment to evaluate at.</param>
	/// <returns>Whether the permissions are in effect, not granted, or granted but suspended by a restriction.</returns>
	internal static Access Check(Connection by, Permissions required, DateTimeOffset now)
	{
		if (!Granted(by).Allows(required)) return Access.NotGranted;
		return Effective(by.Session, now).Allows(required) ? Access.Allowed : Access.Suspended;
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

	/// <summary>Gets a value that indicates whether one user outranks another.</summary>
	/// <param name="actor">The user acting.</param>
	/// <param name="target">The user acted on.</param>
	/// <returns>
	///     <see langword="true" /> if the actor is granted every permission granted to the target and at least one more.
	/// </returns>
	internal static bool Outranks(User actor, User target)
	{
		var mine = actor.Value.Permissions;
		var theirs = target.Value.Permissions;
		return mine.Allows(theirs) && mine != theirs;
	}

	/// <summary>Gets a value that indicates whether a staff action needing a permission may be aimed at a user.</summary>
	/// <param name="by">The connection acting.</param>
	/// <param name="target">The user acted on.</param>
	/// <param name="required">The permission the action needs.</param>
	/// <param name="now">The moment to evaluate at.</param>
	/// <returns>
	///     <see langword="true" /> if the permission is in effect and the actor is the target or outranks them.
	/// </returns>
	internal static bool MayActOn(Connection by, User target, Permissions required, DateTimeOffset now)
	{
		return Allows(by, required, now) && (by.User.Equals(target) || Outranks(by.Session.User, target));
	}
}

/// <summary>Whether a user may do something, given what they are granted and what their restrictions suspend.</summary>
internal enum Access : byte
{
	/// <summary>Every required permission is in effect.</summary>
	Allowed,

	/// <summary>A required permission is not granted.</summary>
	NotGranted,

	/// <summary>Every required permission is granted, but a restriction suspends one of them.</summary>
	Suspended
}