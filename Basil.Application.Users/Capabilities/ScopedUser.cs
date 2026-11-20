using Basil.Application.Shared.Privileges;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Objects.Sessions;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedUser
{
	private readonly UserRegistry? _registry;
	private readonly int _targetId;
	private readonly User? _user;

	public Session Actor { get; }

	public UserPrivileges Effective { get; }

	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Effective, required);
	}

	internal ScopedUser(User user, Session actor)
	{
		Actor = actor;
		Effective = actor.EffectivePrivileges(user.Context.Clock.GetUtcNow());
		Granted = actor.User.Record.Value.Privileges;
		_user = user;
	}

	internal ScopedUser(UserRegistry registry, int targetId, Session actor)
	{
		Actor = actor;
		Effective = actor.EffectivePrivileges(actor.User.Context.Clock.GetUtcNow());
		Granted = actor.User.Record.Value.Privileges;
		_registry = registry;
		_targetId = targetId;
	}

	public ScopedUserProfile Profile => new(this);

	public ScopedUserCredentials Credentials => new(this);

	public ScopedUserSessions Sessions => new(this);

	public ScopedUserPrivileges Privileges => new(this);

	internal UserPrivileges Granted { get; }

	public ScopedUserSilence Silence => new(this);

	public ScopedUserRelationships Relationships => new(this);

	internal bool IsSelf => _user is { } user ? Actor.Record.Equals(user.Record) : _targetId == Actor.Record.Id;

	internal async Task<User?> ResolveAsync(CancellationToken ct = default)
	{
		if (_user is { } resolved) return resolved;

		var users = Actor.User.Context.Users;
		var record = await users.GetAsync(_targetId, ct);
		return record != null && record.Value.DeletedAt is null ? _registry!.Of(record) : null;
	}

	internal bool Outranks(User target)
	{
		return Actor.Outranks(Granted, target.Record.Value.Privileges);
	}

	internal UserActionResult? Deny(UserPrivileges needed)
	{
		return Lacks(needed) ? UserActionResult.Denied : null;
	}

	internal UserActionResult? Guard(User target, bool selfAllowed)
	{
		if (target.Record.Id == BasilBot.Id) return UserActionResult.SystemAccount;
		return (IsSelf && selfAllowed) || Outranks(target) ? null : UserActionResult.NotOutranking;
	}

	internal async Task<bool> IsLockedAsync(Locks area, UserPrivileges manager,
		CancellationToken ct = default)
	{
		return (await Actor.User.Context.Settings.GetAsync(ct)).Locks.HasFlag(area) &&
		       Lacks(manager);
	}

	public async Task<UserActionResult> DeleteAsync()
	{
		if (Deny(UserPrivileges.Owner) is { } denied) return denied;
		if (await ResolveAsync() is not { } user) return UserActionResult.NotFound;
		if (Guard(user, true) is { } refused) return refused;
		return await user.DeleteAsync();
	}
}