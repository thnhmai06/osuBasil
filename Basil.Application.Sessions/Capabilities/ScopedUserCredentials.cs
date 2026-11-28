using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Capabilities;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Sessions.Capabilities;

public readonly struct ScopedUserCredentials(SessionRegistry registry, ScopedUser scoped, Session actor)
{
	public async Task<UserActionResult> MayChangePasswordAsync()
	{
		if (await scoped.IsLockedAsync(Locks.Profiles, UserPrivileges.Owner))
			return UserActionResult.Locked;
		return !scoped.IsSelf && scoped.Deny(UserPrivileges.Owner) is { } denied ? denied : UserActionResult.Ok;
	}

	public async Task<UserActionResult> ChangePasswordAsync(Md5Hash newPasswordHash, Md5Hash? currentPasswordHash,
		bool includeCurrent = false)
	{
		if (await scoped.IsLockedAsync(Locks.Profiles, UserPrivileges.Owner))
			return UserActionResult.Locked;
		if (!scoped.IsSelf && scoped.Deny(UserPrivileges.Owner) is { } denied) return denied;
		if (await scoped.ResolveAsync() is not { } user) return UserActionResult.NotFound;
		if (scoped.Guard(user, true) is { } refused) return refused;
		return await registry.Of(user.Record).Credentials.ChangeAsync(newPasswordHash,
			currentPasswordHash, actor, includeCurrent);
	}
}