using Basil.Application.Common;
using Basil.Application.Common.Settings;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>An account scoped to one connection and its authority snapshot.</summary>
/// <remarks>
///     The snapshot is for one request or command; keeping it longer lets its privileges outlive changes to privileges
///     or a silence. A scope made from a <see cref="UserReference" /> reads nothing until the caller's privilege to act
///     has been checked, so a refusal is the same whether or not the account exists.
/// </remarks>
public readonly struct ScopedUser
{
	private readonly UserRegistry? _registry;
	private readonly UserReference _target;
	private readonly User? _user;

	/// <summary>Gets the actor this scope is for.</summary>
	public Connection Actor { get; }

	/// <summary>Gets the privileges the actor held when the scope was created.</summary>
	public UserPrivileges Effective { get; }

	/// <summary>Checks whether the actor holds the privilege an action needs.</summary>
	/// <param name="required">The privilege the action needs.</param>
	/// <returns><see langword="null" /> when it is held; otherwise what is missing.</returns>
	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Effective, required);
	}

	/// <summary>Creates a scoped account for a connection.</summary>
	/// <param name="user">The account to scope.</param>
	/// <param name="actor">The connection acting on the account.</param>
	internal ScopedUser(User user, Connection actor)
	{
		Actor = actor;
		Effective = actor.EffectivePrivileges(user.Context.Clock.GetUtcNow());
		Granted = actor.User.Record.Value.Privileges;
		_user = user;
	}

	/// <summary>Creates a scope over an account that is only referenced and is read once the caller is authorized.</summary>
	/// <param name="registry">The registry that hands out the account.</param>
	/// <param name="target">The account the caller refers to.</param>
	/// <param name="actor">The connection acting on the account.</param>
	internal ScopedUser(UserRegistry registry, UserReference target, Connection actor)
	{
		Actor = actor;
		Effective = actor.EffectivePrivileges(actor.User.Context.Clock.GetUtcNow());
		Granted = actor.User.Record.Value.Privileges;
		_registry = registry;
		_target = target;
	}

	/// <summary>The profile operations for this account.</summary>
	public ScopedUserProfile Profile => new(this);

	/// <summary>The credential operations for this account.</summary>
	public ScopedUserCredentials Credentials => new(this);

	/// <summary>The session operations for this account.</summary>
	public ScopedUserSessions Sessions => new(this);

	/// <summary>The privilege operations for this account.</summary>
	public ScopedUserPrivileges Privileges => new(this);

	/// <summary>The privileges granted to the connection's user when this scope was created.</summary>
	internal UserPrivileges Granted { get; }

	/// <summary>The silence operations for this account.</summary>
	public ScopedUserSilence Silence => new(this);

	/// <summary>The friends and blocks this account sets toward other users.</summary>
	public ScopedUserRelationships Relationships => new(this);

	/// <summary>Whether the caller is acting on their own account.</summary>
	internal bool IsSelf => _user is { } user ? Actor.Record.Equals(user.Record) : _target.Names(Actor.Record);

	/// <summary>Gets the account this scope is over.</summary>
	/// <param name="ct">A token that cancels the read.</param>
	/// <returns>The account, or <see langword="null" /> when no live account has the referenced id or name.</returns>
	internal async Task<User?> ResolveAsync(CancellationToken ct = default)
	{
		if (_user is { } resolved) return resolved;

		var users = Actor.User.Context.Users;
		var record = _target.Id is { } id
			? await users.GetAsync(id, ct)
			: _target.Name is { } name
				? await users.GetByNameAsync(name, ct)
				: null;
		return record is { } found && found.Value.DeletedAt is null ? _registry!.Of(found) : null;
	}

	/// <summary>Whether the caller's granted privileges strictly outrank an account's.</summary>
	/// <param name="target">The account acted on.</param>
	internal bool Outranks(User target)
	{
		return Actor.Outranks(Granted, target.Record.Value.Privileges);
	}

	/// <summary>Refuses an action when the caller lacks the privilege it needs.</summary>
	/// <param name="needed">The privilege required.</param>
	/// <returns><see langword="null" /> when the privilege is held; otherwise the refusal naming what is missing.</returns>
	internal UserOutcome? Deny(UserPrivileges needed)
	{
		return Lacks(needed) ? UserOutcome.Denied : null;
	}

	/// <summary>Checks the account hierarchy for an action aimed at a resolved account.</summary>
	/// <param name="target">The account acted on.</param>
	/// <param name="selfAllowed">Whether the caller may act on their own account.</param>
	/// <returns><see langword="null" /> when allowed; otherwise, why the operation is unavailable.</returns>
	internal UserOutcome? Guard(User target, bool selfAllowed)
	{
		if (target.Record.IsBasilBot()) return UserOutcome.SystemAccount;
		return (IsSelf && selfAllowed) || Outranks(target) ? null : UserOutcome.NotOutranking;
	}

	/// <summary>Checks whether an area is locked for the caller.</summary>
	/// <param name="area">The area to check.</param>
	/// <param name="manager">The privilege that manages the area and bypasses its lock.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when the area is locked and the caller does not hold <paramref name="manager" />.</returns>
	internal async Task<bool> IsLockedAsync(Locks area, UserPrivileges manager,
		CancellationToken ct = default)
	{
		return (await Actor.User.Context.Settings.GetAsync(ct)).Locks.HasFlag(area) &&
		       Lacks(manager);
	}

	/// <summary>Deletes this account, keeping it out of sign-in and every listing while its record remains.</summary>
	/// <returns>The outcome of deleting the account.</returns>
	public async Task<UserOutcome> DeleteAsync()
	{
		if (Deny(UserPrivileges.Owner) is { } denied) return denied;
		if (await ResolveAsync() is not { } user) return UserOutcome.NotFound;
		if (Guard(user, true) is { } refused) return refused;
		return await user.DeleteAsync();
	}
}