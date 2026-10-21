using Basil.Application.Runtime.Sessions;
using Basil.Domain.Permissions;

namespace Basil.Application.Runtime;

/// <summary>
///     A short-lived capability over one object for one connection: the object's behaviour as that connection may do it.
/// </summary>
/// <remarks>
///     The caller's effective permissions are snapshotted once when the scope is created; a scope serves one request or
///     command and is then dropped, never kept. Implementations state what each behaviour needs through
///     <see cref="Require" /> and return its refusal; they never read permissions themselves.
/// </remarks>
/// <typeparam name="TOwner">The object this scope is over.</typeparam>
public abstract class Scoped<TOwner>
{
	/// <summary>Creates a scope over an object for a connection.</summary>
	/// <param name="owner">The object to scope.</param>
	/// <param name="by">The connection acting on the object.</param>
	/// <param name="clock">The clock the caller's restrictions are evaluated against.</param>
	protected Scoped(TOwner owner, Connection by, TimeProvider clock)
	{
		Owner = owner;
		By = by;
		Permissions = by.User.EffectivePermissions(clock.GetUtcNow());
	}

	/// <summary>Gets the object this scope is over.</summary>
	public TOwner Owner { get; }

	/// <summary>Gets the connection this scope is for.</summary>
	public Connection By { get; }

	/// <summary>Gets the permissions of the caller that were in effect when the scope was created.</summary>
	public Permissions Permissions { get; }

	/// <summary>Checks whether the caller holds every permission an action needs.</summary>
	/// <param name="required">The permissions the action needs.</param>
	/// <returns><see langword="null" /> when all are in effect; otherwise the permissions that are missing.</returns>
	protected Permissions? Require(Permissions required)
	{
		var missing = required & ~Permissions;
		return missing == Permissions.None ? null : missing;
	}
}
