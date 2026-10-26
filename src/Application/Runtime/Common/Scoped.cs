using System.Reflection;
using Basil.Application.Runtime.Sessions;

namespace Basil.Application.Runtime.Common;

/// <summary>
///     A short-lived capability over one object for one connection: the object's behaviour as that connection may do it.
/// </summary>
/// <remarks>
///     The permissions the connection holds are snapshotted once when the scope is created; a scope serves one request or
///     command and is then dropped, never kept. Implementations state what each behaviour needs through
///     <see cref="Require" /> and return its refusal; they never compare permissions themselves. How a requirement is
///     compared depends on the enum: a <see cref="FlagsAttribute" /> enum is met when every required bit is held, any
///     other enum is ordered levels and is met when the held value is greater than or equal to the required one.
/// </remarks>
/// <typeparam name="TPermissions">
///     The enum of the permissions the scope checks: <c>Permissions</c> for the caller's effective permissions, a room scope's
///     own room permissions, or any other flags or levels enum.
/// </typeparam>
/// <remarks>Creates a scope for a connection with the permissions it holds.</remarks>
/// <param name="by">The connection acting.</param>
/// <param name="permissions">The permissions the connection holds, as they are when the scope is created.</param>
public abstract class Scoped<TPermissions>(Connection by, TPermissions permissions) where TPermissions : struct, Enum
{
	private static readonly bool IsFlagsEnum = typeof(TPermissions).GetCustomAttribute<FlagsAttribute>() is not null;

	/// <summary>Gets the connection this scope is for.</summary>
	public Connection By { get; } = by;

	/// <summary>Gets the permissions the connection held when the scope was created.</summary>
	public TPermissions Permissions { get; } = permissions;

	/// <summary>Checks whether the connection holds the right an action needs.</summary>
	/// <param name="required">The right the action needs.</param>
	/// <returns>
	///     <see langword="null" /> when it is held; otherwise what is missing: the bits not held for a flags enum, the
	///     required level for any other enum.
	/// </returns>
	protected TPermissions? Require(TPermissions required)
	{
		var held = Convert.ToInt64(Permissions);
		var needed = Convert.ToInt64(required);

		if (!IsFlagsEnum)
			return held >= needed ? null : required;

		var missing = needed & ~held;
		return missing == 0 ? null : (TPermissions)Enum.ToObject(typeof(TPermissions), missing);
	}
}
