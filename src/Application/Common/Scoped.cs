using System.Reflection;

namespace Basil.Application.Common;

/// <summary>
///     The comparison every capability view makes when an action needs a right: a capability is a short-lived value over
///     one object for one actor, and this is how it states what an action needs.
/// </summary>
/// <remarks>
///     The privileges an actor holds are snapshotted once when the capability is created; a capability serves one
///     request or command and is then dropped, never kept. A <see cref="FlagsAttribute" /> enum is met when every
///     required bit is held; any other enum is ordered levels and is met when the held value is greater than or equal to
///     the required one.
/// </remarks>
public static class ScopedRights
{
	/// <summary>Checks whether the held privileges meet the ones an action needs.</summary>
	/// <typeparam name="TRights">The enum of the rights that are checked.</typeparam>
	/// <param name="held">The privileges the actor holds.</param>
	/// <param name="required">The privileges the action needs.</param>
	/// <returns>
	///     <see langword="null" /> when they are held; otherwise what is missing: the bits not held for a flags enum, the
	///     required level for any other enum.
	/// </returns>
	public static TRights? Require<TRights>(TRights held, TRights required)
		where TRights : struct, Enum
	{
		var have = Convert.ToInt64(held);
		var needed = Convert.ToInt64(required);

		if (!Flags<TRights>.IsFlags)
			return have >= needed ? null : required;

		var missing = needed & ~have;
		return missing == 0 ? null : (TRights)Enum.ToObject(typeof(TRights), missing);
	}

	private static class Flags<TRights> where TRights : struct, Enum
	{
		/// <summary>Whether the rights type is a flags enum.</summary>
		public static readonly bool IsFlags = typeof(TRights).GetCustomAttribute<FlagsAttribute>() is not null;
	}
}