using Basil.Application.Sessions.Persistence;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Ports;
using Basil.Domain.Sessions;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Users.Logic;

internal sealed class AccountRules(
	IUserRepository users,
	ICredentialRepository credentials,
	ISettingsRepository settings,
	ILogger<AccountRules> logger) : IAccountRules
{
	public async Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt,
		CancellationToken ct)
	{
		if ((await settings.GetAsync(ct)).Locks.HasFlag(Locks.Accounts))
			return RegistrationFailure.Locked;

		_ = new UserData { Name = attempt.Username };

		return await users.GetByNameAsync(attempt.Username, ct) is not null
			? RegistrationFailure.NameTaken
			: null;
	}

	public async Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAsync(UserData data, Md5Hash passwordHash,
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

	public async Task<(UserRecord? User, LoginFailure? Failure)> FindLoginUserAsync(LoginAttempt attempt,
		CancellationToken ct)
	{
		var user = await users.GetByNameAsync(attempt.Username, ct);
		if (user is null || user.Id == BasilBot.Id) return (null, LoginFailure.UnknownUser);
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