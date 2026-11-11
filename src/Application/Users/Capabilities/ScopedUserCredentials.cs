using Basil.Application.Shared;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Objects;
using Basil.Domain.Shared;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>Changes an account's password according to the caller's authority.</summary>
public readonly struct ScopedUserCredentials(ScopedUser scoped)
{
	/// <summary>Replaces this account's password and signs its other sessions out.</summary>
	/// <param name="newPasswordHash">The MD5 digest of the new password.</param>
	/// <param name="currentPasswordHash">The current digest, required when changing one's own password.</param>
	/// <param name="includeCurrent">
	///     Whether the HTTP API session making this call is signed out too; ignored when acting on another account.
	/// </param>
	/// <returns>The outcome of replacing the password.</returns>
	/// <remarks>
	///     A user's own password change needs the current password and signs out every other session. A new password
	///     equal to the current one signs nobody out. While <see cref="Locks.Profiles" /> is locked, only a user
	///     holding <see cref="UserPrivileges.Owner" /> may change a password. Changing another account's password
	///     needs <see cref="UserPrivileges.Owner" /> and hierarchy, signs out all of its sessions, and is recorded
	///     in the moderation log.
	/// </remarks>
	public async Task<UserOutcome> ChangePasswordAsync(Md5 newPasswordHash, Md5? currentPasswordHash,
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