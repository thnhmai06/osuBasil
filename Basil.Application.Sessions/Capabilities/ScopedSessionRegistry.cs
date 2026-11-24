using Basil.Application.Sessions.Registry;
using Basil.Application.Shared.Privileges;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Sessions.Capabilities;

public readonly struct ScopedSessionRegistry
{
	public Session Actor { get; }

	public UserPrivileges Privileges { get; }

	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Privileges, required);
	}

	private readonly AccountRegistration _registration;
	private readonly SessionContext _context;

	internal ScopedSessionRegistry(AccountRegistration registration, SessionContext context, Session actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(context.Clock.GetUtcNow());
		_registration = registration;
		_context = context;
	}

	public async Task<(UserRecord? User, UserActionResult Result)> CreateAccountAsync(UserData data,
		Md5Hash passwordHash, UserPrivileges? privileges)
	{
		var granted = privileges ??
		              (await _context.Settings.GetAsync()).Accounts.DefaultPrivileges;
		if (Lacks(UserPrivileges.Owner | granted))
			return (null, UserActionResult.Denied);

		var (user, outcome) = await _registration.CreateAsync(data, passwordHash, granted);
		return (user, outcome);
	}
}