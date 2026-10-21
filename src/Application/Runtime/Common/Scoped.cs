using System.Reflection;
using Basil.Application.Runtime.Sessions;

namespace Basil.Application.Runtime;

/// <summary>
///     A short-lived capability over one object for one connection: the object's behaviour as that connection may do it.
/// </summary>
/// <remarks>
///     The rights the connection holds are snapshotted once when the scope is created; a scope serves one request or
///     command and is then dropped, never kept. Implementations state what each behaviour needs through
///     <see cref="Require" /> and return its refusal; they never compare rights themselves. How a requirement is
///     compared depends on the enum: a <see cref="FlagsAttribute" /> enum is met when every required bit is held, any
///     other enum is ordered levels and is met when the held value is greater than or equal to the required one.
/// </remarks>
/// <typeparam name="TRights">
///     The enum of the rights the scope checks: <c>Permissions</c> for the caller's effective permissions, a room scope's
///     own room rights, or any other flags or levels enum.
/// </typeparam>
public abstract class Scoped<TRights> where TRights : struct, Enum
{
	private static readonly bool IsFlags = typeof(TRights).GetCustomAttribute<FlagsAttribute>() is not null;

	/// <summary>Creates a scope for a connection with the rights it holds.</summary>
	/// <param name="by">The connection acting.</param>
	/// <param name="rights">The rights the connection holds, as they are when the scope is created.</param>
	protected Scoped(Connection by, TRights rights)
	{
		By = by;
		Rights = rights;
	}

	/// <summary>Gets the connection this scope is for.</summary>
	public Connection By { get; }

	/// <summary>Gets the rights the connection held when the scope was created.</summary>
	public TRights Rights { get; }

	/// <summary>Checks whether the connection holds the right an action needs.</summary>
	/// <param name="required">The right the action needs.</param>
	/// <returns>
	///     <see langword="null" /> when it is held; otherwise what is missing: the bits not held for a flags enum, the
	///     required level for any other enum.
	/// </returns>
	protected TRights? Require(TRights required)
	{
		var held = Convert.ToInt64(Rights);
		var needed = Convert.ToInt64(required);

		if (!IsFlags)
			return held >= needed ? null : required;

		var missing = needed & ~held;
		return missing == 0 ? null : (TRights)Enum.ToObject(typeof(TRights), missing);
	}
}
