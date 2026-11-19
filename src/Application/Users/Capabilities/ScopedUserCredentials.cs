using Basil.Application.Shared.Settings;
using Basil.Domain.Users;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedUserCredentials(ScopedUser scoped)
{
	public async Task<UserOutcome> ChangePasswordAsync(Md5Hash newPasswordHash, Md5Hash? currentPasswordHash,
		bool includeCurrent = false)
	{
		if (await scoped.IsLockedAsync(Locks.Profiles, UserPrivileges.Owner))
			return UserOutcome.Locked;
		if (!scoped.IsSelf && scoped.Deny(UserPrivileges.Owner) is { } denied) return denied;
		if (await scoped.ResolveAsync() is not { } user) return UserOutcome.NotFound;
		if (scoped.Guard(user, true) is { } refused) return refused;
		return await user.Credentials.ChangeAsync(newPasswordHash, currentPasswordHash, scoped.Actor,
			includeCurrent);
	}
}