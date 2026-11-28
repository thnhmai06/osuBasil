using Basil.Application.Sessions.Persistence;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Persistence;
using Basil.Domain.Sessions;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Sessions.Logic;

/// <summary>Checks and creates accounts, and finds the account a sign-in names.</summary>
internal sealed class AccountRules(
	IUserRepository users,
	ICredentialRepository credentials,
	ISettingsRepository settings,
	ILogger<AccountRules> logger)
{
	/// <summary>Checks whether an account could be registered with the attempt, without creating it.</summary>
	/// <returns>Why it could not, or null when it could.</returns>
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

	/// <summary>Creates an account with its credential; a new account holds the given privileges, or the default ones when none are given.</summary>
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

	/// <summary>Finds the account a sign-in names and checks that it may sign in and that the password matches.</summary>
	public async Task<(UserRecord? User, LoginFailure? Failure)> FindLoginUserAsync(LoginAttempt attempt,
		CancellationToken ct)
	{
		var user = await users.GetByNameAsync(attempt.Username, ct);
		if (user is null) return (null, LoginFailure.UnknownUser);
		switch (user.CheckSignIn())
		{
			case UserSignInResult.Denied: return (null, LoginFailure.UnknownUser);
			case UserSignInResult.AccountDeleted: return (null, LoginFailure.AccountDeleted);
		}

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