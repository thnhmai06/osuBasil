using Basil.Application.Shared.Privileges;
using Basil.Application.Users.Objects.Sessions;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedUserRegistry
{
	public Session Actor { get; }

	public UserPrivileges Privileges { get; }

	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Privileges, required);
	}

	private readonly UserRegistry _registry;

	internal ScopedUserRegistry(UserRegistry registry, Session actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(actor.User.Context.Clock.GetUtcNow());
		_registry = registry;
	}

	public async Task<(UserRecord? User, UserActionResult Result)> CreateAccountAsync(UserData data,
		Md5Hash passwordHash, UserPrivileges? privileges)
	{
		var granted = privileges ??
		              (await Actor.User.Context.Settings.GetAsync()).Accounts.DefaultPrivileges;
		if (Lacks(UserPrivileges.Owner | granted))
			return (null, UserActionResult.Denied);

		var (user, outcome) = await _registry.RegisterAsync(data, passwordHash, granted);
		return (user, outcome);
	}
}