using System.Reflection;

namespace Basil.Application.Shared.Privileges;

/// <summary>
///     The comparison every capability view makes when an action needs a privilege: a capability is a short-lived value
///     over
///     one object for one actor, and this is how it states what an action needs.
/// </summary>
/// <remarks>
///     The privileges an actor holds are snapshotted once when the capability is created; a capability serves one
///     request or command and is then dropped, never kept. A <see cref="FlagsAttribute" /> enum is met when every
///     required bit is held; any other enum is ordered levels and is met when the held value is greater than or equal to
///     the required one.
/// </remarks>
public static class ScopedPrivileges
{
	/// <summary>Checks whether the held privileges fall short of the ones an action needs.</summary>
	/// <typeparam name="TPrivileges">The enum of the privileges that are checked.</typeparam>
	/// <param name="held">The privileges the actor holds.</param>
	/// <param name="required">The privileges the action needs.</param>
	/// <returns><see langword="true" /> when something required is not held; <see langword="false" /> when all are met.</returns>
	public static bool Lacks<TPrivileges>(TPrivileges held, TPrivileges required)
		where TPrivileges : struct, Enum
	{
		var have = Convert.ToInt64(held);
		var needed = Convert.ToInt64(required);

		return Flags<TPrivileges>.IsFlags ? (needed & ~have) != 0 : have < needed;
	}

	private static class Flags<TPrivileges> where TPrivileges : struct, Enum
	{
		/// <summary>Whether the privileges type is a flags enum.</summary>
		public static readonly bool IsFlags = typeof(TPrivileges).GetCustomAttribute<FlagsAttribute>() is not null;
	}
}