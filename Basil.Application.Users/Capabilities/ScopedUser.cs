using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Privileges;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedUser
{
	private readonly UserRegistry _registry;
	private readonly int _targetId;
	private readonly User? _user;

	public IActor Actor { get; }

	public UserPrivileges Effective { get; }

	public bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Effective, required);
	}

	internal ScopedUser(User user, IActor actor)
	{
		Actor = actor;
		Effective = actor.EffectivePrivileges(user.Context.Clock.GetUtcNow());
		_registry = user.Context.Registry;
		_user = user;
	}

	internal ScopedUser(UserRegistry registry, int targetId, IActor actor)
	{
		Actor = actor;
		Effective = actor.EffectivePrivileges(registry.Context.Clock.GetUtcNow());
		_registry = registry;
		_targetId = targetId;
	}

	public ScopedUserProfile Profile => new(this);

	public ScopedUserPrivileges Privileges => new(this);

	public ScopedUserSilence Silence => new(this);

	public ScopedUserRelationships Relationships => new(this);

	/// <summary>The account of the actor, for what the actor does on its own behalf.</summary>
	internal User ActorUser => _registry.Of(Actor.Record);

	/// <summary>Whether the actor outranks even an account with its own privileges, which only the console's operator does.</summary>
	internal bool IsOperator => Actor.Outranks(Effective, Effective);

	public bool IsSelf => _user is { } user ? Actor.Record.Equals(user.Record) : _targetId == Actor.Record.Id;

	public async Task<User?> ResolveAsync(CancellationToken ct = default)
	{
		if (_user is { } resolved) return resolved;

		var users = _registry.Context.Users;
		var record = await users.GetAsync(_targetId, ct);
		return record != null && record.Value.DeletedAt is null ? _registry.Of(record) : null;
	}

	/// <summary>The same actor aimed at another account.</summary>
	internal ScopedUser Other(int targetId)
	{
		return new ScopedUser(_registry, targetId, Actor);
	}

	internal bool Outranks(User target)
	{
		return Actor.Outranks(Effective, target.Record.Value.Privileges);
	}

	public UserActionResult? Deny(UserPrivileges needed)
	{
		return Lacks(needed) ? UserActionResult.Denied : null;
	}

	public UserActionResult? Guard(User target, bool selfAllowed)
	{
		if (target.Record.Id == BasilBot.Id) return UserActionResult.SystemAccount;
		return (IsSelf && selfAllowed) || Outranks(target) ? null : UserActionResult.NotOutranking;
	}

	public async Task<bool> IsLockedAsync(Locks area, UserPrivileges manager,
		CancellationToken ct = default)
	{
		return (await _registry.Context.Settings.GetAsync(ct)).Locks.HasFlag(area) &&
		       Lacks(manager);
	}

	/// <summary>Applies a change to the account: every refusal known beforehand is answered before anything changes, then the name, country, away message, private-message setting and privileges are set in that order.</summary>
	/// <param name="change">What to change.</param>
	public async Task<UserActionResult> UpdateAsync(UserChange change)
	{
		User? user = null;
		if (change.Name is not null || change.Country is not null)
		{
			var (guarded, refusal) = await Profile.GuardAsync();
			if (guarded is null) return refusal;

			user = guarded;
		}

		if ((change.Away is not null || change.PmPrivate is not null) && this is { IsSelf: false, IsOperator: false })
			return UserActionResult.NotSelf;

		var privileges = change.Privileges?.Completed();
		if (privileges is not null)
		{
			if (Deny(UserPrivileges.Owner) is { } denied) return denied;
			if (await ResolveAsync() is not { } target) return UserActionResult.NotFound;
			if (Guard(target, true) is { } refused) return refused;
			if (Deny(target.Record.Value.Privileges ^ privileges.Value) is { } cannotGrant) return cannotGrant;

			user ??= target;
		}

		user ??= await ResolveAsync();
		if (user is null) return UserActionResult.NotFound;

		if (change.Name is not null && await user.Profile.RenameAsync(change.Name) is var renamed and not UserActionResult.Ok)
			return renamed;
		if (change.Country is { } country) await user.Profile.SetCountryAsync(country);
		if (change.Away is not null) user.Profile.SetAway(change.Away.Length == 0 ? null : change.Away);
		if (change.PmPrivate is { } pmPrivate) user.Profile.SetPmPrivate(pmPrivate);
		if (privileges is { } set) await user.SetPrivilegesAsync(set);
		return UserActionResult.Ok;
	}

	public async Task<UserActionResult> DeleteAsync()
	{
		if (Deny(UserPrivileges.Owner) is { } denied) return denied;
		if (await ResolveAsync() is not { } user) return UserActionResult.NotFound;
		if (Guard(user, true) is { } refused) return refused;
		return await user.DeleteAsync();
	}
}