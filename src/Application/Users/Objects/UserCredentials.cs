using Basil.Application.Authentication.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Common;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

/// <summary>Owns the password of one account.</summary>
public sealed class UserCredentials
{
	private readonly User _user;

	/// <summary>Creates the credential state for an account.</summary>
	/// <param name="user">The account that owns the password.</param>
	internal UserCredentials(User user)
	{
		_user = user;
	}

	/// <summary>Checks whether a claimed password is the account's password.</summary>
	/// <param name="passwordHash">The MD5 digest of the claimed password.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when the password is the account's.</returns>
	internal Task<bool> IsPasswordAsync(Md5 passwordHash, CancellationToken cancellationToken)
	{
		return _user.Context.Credentials.IsPasswordAsync(_user.Record, passwordHash, cancellationToken);
	}

	/// <summary>Replaces the account's password.</summary>
	/// <param name="passwordHash">The MD5 digest of the new password.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	internal Task ReplaceAsync(Md5 passwordHash, CancellationToken cancellationToken)
	{
		return _user.Context.Credentials.CreateOrUpdateAsync(new Credentials(_user.Record, passwordHash),
			cancellationToken);
	}

	/// <summary>Replaces the account's password and signs its other sessions out.</summary>
	/// <param name="newPasswordHash">The MD5 digest of the new password.</param>
	/// <param name="currentPasswordHash">The current digest, required when the account changes its own password.</param>
	/// <param name="actor">The connection changing the password.</param>
	/// <param name="includeCurrent">Whether the HTTP API session of <paramref name="actor" /> is signed out too.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of replacing the password.</returns>
	/// <remarks>
	///     A new password equal to the current one signs nobody out. Changing another account's password is recorded
	///     in the moderation log.
	/// </remarks>
	internal async Task<UserOutcome> ChangeAsync(Md5 newPasswordHash, Md5? currentPasswordHash, Connection actor,
		bool includeCurrent, CancellationToken cancellationToken)
	{
		var isSelf = actor.Record.Equals(_user.Record);
		var unchanged = false;
		if (isSelf)
		{
			if (currentPasswordHash is not { } current || !await IsPasswordAsync(current, cancellationToken))
				return UserOutcome.WrongPassword;

			unchanged = current == newPasswordHash;
		}

		await ReplaceAsync(newPasswordHash, cancellationToken);
		if (unchanged) return UserOutcome.Ok;

		var keep = isSelf && !includeCurrent && actor is ApiConnection api ? api.Session.Id : (Guid?)null;
		await _user.Sessions.CloseAsync(null, keep, SessionEndReason.CredentialsChanged, cancellationToken);
		if (!isSelf)
			await _user.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.ChangePassword,
				Actor = actor.Record,
				Target = _user.Record,
				Timestamp = _user.Context.Clock.GetUtcNow()
			}, cancellationToken);

		return UserOutcome.Ok;
	}
}