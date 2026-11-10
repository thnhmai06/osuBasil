using Basil.Application.Authentication.Persistence;
using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Ports;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Users.Logic;

/// <inheritdoc />
internal sealed class AccountRules(
	IUserRepository users,
	ICredentialRepository credentials,
	ISettingsRepository settings,
	ILogger<AccountRules> logger) : IAccountRules
{
	/// <inheritdoc />
	public async Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt,
		CancellationToken ct)
	{
		if ((await settings.GetAsync(ct)).Locks.HasFlag(Locks.Accounts))
			return RegistrationFailure.Locked;

		if (!UserData.IsValidName(attempt.Username)) return RegistrationFailure.InvalidName;

		return await users.GetByNameAsync(attempt.Username, ct) is not null
			? RegistrationFailure.NameTaken
			: null;
	}

	/// <inheritdoc />
	public async Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAsync(UserData data, Md5 passwordHash,
		UserPrivileges? privileges)
	{
		data.Privileges = privileges ?? (await settings.GetAsync()).Accounts.DefaultPrivileges;
		UserRecord user;
		try
		{
			user = await users.CreateAsync(data);
		}
		catch (AlreadyExistsException)
		{
			return (null, RegistrationFailure.NameTaken);
		}

		try
		{
			await credentials.CreateOrUpdateAsync(new Credentials(user, passwordHash));
		}
		catch
		{
			// An account without a credential can never sign in and would hold its name: take it back.
			await DiscardAsync(user);
			throw;
		}

		return (user, null);
	}

	/// <inheritdoc />
	public async Task<(UserRecord? User, LoginFailure? Failure)> FindLoginUserAsync(LoginAttempt attempt,
		CancellationToken ct)
	{
		var user = await users.GetByNameAsync(attempt.Username, ct);
		if (user is null || user.IsBasilBot()) return (null, LoginFailure.UnknownUser);
		if (user.Value.DeletedAt is not null) return (null, LoginFailure.AccountDeleted);
		if (!await credentials.IsPasswordAsync(user, attempt.PasswordHash, ct))
			return (null, LoginFailure.WrongPassword);

		return (user, null);
	}

	private async Task DiscardAsync(UserRecord user)
	{
		try
		{
			await users.DiscardAsync(user);
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Could not discard user {UserId} whose credential was not stored", user.Id);
		}
	}
}