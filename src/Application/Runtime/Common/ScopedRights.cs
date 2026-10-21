using System.Reflection;
using Basil.Application.Runtime.Sessions;

namespace Basil.Application.Runtime;

/// <summary>
///     A <see cref="Scoped{TOwner}" /> whose caller also holds a right of its own kind over the owner: a role-derived set
///     of rights, or a level.
/// </summary>
/// <remarks>
///     How a requirement is compared depends on the enum: a <see cref="FlagsAttribute" /> enum is met when every
///     required bit is held, any other enum is met when the held value is greater than or equal to the required one
///     (its members are ordered levels). The right is fixed when the scope is created, like the permissions.
/// </remarks>
/// <typeparam name="TOwner">The object this scope is over.</typeparam>
/// <typeparam name="TRights">The enum of the rights; flags or ordered levels.</typeparam>
public abstract class Scoped<TOwner, TRights> : Scoped<TOwner> where TRights : struct, Enum
{
	private static readonly bool IsFlags = typeof(TRights).GetCustomAttribute<FlagsAttribute>() is not null;

	/// <inheritdoc />
	protected Scoped(TOwner owner, Connection by, TimeProvider clock) : base(owner, by, clock)
	{
	}

	/// <summary>Gets the rights the caller holds over the owner; set once by the implementation when it is created.</summary>
	public TRights Rights { get; protected init; }

	/// <summary>Checks whether the caller holds the right an action needs.</summary>
	/// <param name="required">The right the action needs.</param>
	/// <returns>
	///     <see langword="null" /> when it is held; otherwise what is missing: the bits not held for a flags enum, the
	///     required level for any other enum.
	/// </returns>
	protected TRights? RequireRight(TRights required)
	{
		var held = Convert.ToInt64(Rights);
		var needed = Convert.ToInt64(required);

		if (!IsFlags)
			return held >= needed ? null : required;

		var missing = needed & ~held;
		return missing == 0 ? null : (TRights)Enum.ToObject(typeof(TRights), missing);
	}
}
