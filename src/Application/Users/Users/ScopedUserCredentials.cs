using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Connections;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Users.Users;

/// <summary>Changes an account's password according to the caller's authority.</summary>
public sealed class ScopedUserCredentials(ScopedUser scoped)
{
	/// <summary>Replaces this account's password and signs its other sessions out.</summary>
	/// <param name="newPasswordHash">The MD5 digest of the new password.</param>
	/// <param name="currentPasswordHash">The current digest, required when changing one's own password.</param>
	/// <param name="includeCurrent">
	///     Whether the HTTP API session making this call is signed out too; ignored when acting on another account.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of replacing the password.</returns>
	/// <remarks>
	///     A user's own password change needs the current password and signs out every other session. A new password
	///     equal to the current one signs nobody out. While <see cref="Locks.Profiles" /> is locked, only a user
	///     holding <see cref="UserPermissions.UserManage" /> may change a password. Changing another account's password
	///     needs <see cref="UserPermissions.UserManage" /> and hierarchy, signs out all of its sessions, and is recorded
	///     in the moderation log.
	/// </remarks>
	public async Task<Result<UserOutcome>> ChangePasswordAsync(Md5 newPasswordHash, Md5? currentPasswordHash,
		bool includeCurrent = false, CancellationToken cancellationToken = default)
	{
		if (scoped.User.Record.IsBasilBot()) return UserOutcome.SystemAccount;
		if (await scoped.IsLockedAsync(Locks.Profiles, UserPermissions.UserManage, cancellationToken))
			return UserOutcome.Locked;

		var user = scoped.User;
		var unchanged = false;
		if (scoped.IsSelf)
		{
			if (currentPasswordHash is not { } current ||
			    !await user.Credentials.IsPasswordAsync(current, cancellationToken))
				return UserOutcome.WrongPassword;

			unchanged = current == newPasswordHash;
		}
		else if (scoped.Guard(UserPermissions.UserManage, false) is { } denied)
		{
			return denied;
		}

		await user.Credentials.ReplaceAsync(newPasswordHash, cancellationToken);
		if (unchanged) return UserOutcome.Ok;

		await ScopedUserSessions.CloseCore(user, null, SessionToKeep(includeCurrent), SessionEndReason.CredentialsChanged,
			!scoped.IsSelf, cancellationToken);
		if (!scoped.IsSelf) await LogChangeAsync(user, cancellationToken);

		return UserOutcome.Ok;
	}

	private Guid? SessionToKeep(bool includeCurrent)
	{
		return scoped.IsSelf && !includeCurrent && scoped.By is ApiConnection api ? api.Session.Id : null;
	}

	private async Task LogChangeAsync(User user, CancellationToken cancellationToken)
	{
		await user.Context.ModerationLog.AppendAsync(new ModerationData
		{
			Action = ModerationAction.ChangePassword,
			By = scoped.By.Record,
			Target = user.Record,
			Timestamp = user.Context.Clock.GetUtcNow()
		}, cancellationToken);
	}
}
