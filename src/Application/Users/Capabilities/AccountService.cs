using Basil.Application.Auth.Persistence;
using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>Applies account rules and persists account credentials.</summary>
internal sealed class AccountService(
	IUserRepository users,
	ICredentialRepository credentials,
	ISettingsRepository settings)
{
	/// <summary>Checks whether an account may be registered with the given attempt.</summary>
	/// <param name="attempt">The registration the client asked for.</param>
	/// <param name="cancellationToken">A token that cancels the check.</param>
	/// <returns><see langword="null" /> when the attempt may be registered; otherwise the reason it may not.</returns>
	public async Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt,
		CancellationToken cancellationToken)
	{
		if ((await settings.GetAsync(cancellationToken)).Locks.HasFlag(Locks.Accounts))
			return RegistrationFailure.Locked;

		try
		{
			_ = new UserData { Name = attempt.Username };
		}
		catch (ArgumentException)
		{
			return RegistrationFailure.InvalidName;
		}

		return await users.GetByNameAsync(attempt.Username, cancellationToken) is not null
			? RegistrationFailure.NameTaken
			: null;
	}

	/// <summary>Creates an account with the given data and its password.</summary>
	/// <param name="data">The data of the new account.</param>
	/// <param name="passwordHash">The password hash of the account.</param>
	/// <param name="cancellationToken">A token that cancels the creation.</param>
	/// <returns>The created account, or <see langword="null" /> and the reason when the name is taken.</returns>
	public async Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAsync(UserData data, Md5 passwordHash,
		CancellationToken cancellationToken)
	{
		UserRecord user;
		try
		{
			user = await users.CreateAsync(data, cancellationToken);
		}
		catch (AlreadyExistsException)
		{
			return (null, RegistrationFailure.NameTaken);
		}

		await credentials.CreateOrUpdateAsync(new Credentials(user, passwordHash), cancellationToken);
		return (user, null);
	}

	/// <summary>Finds the account a login attempt names and checks its password.</summary>
	/// <param name="attempt">The login the client sent.</param>
	/// <param name="cancellationToken">A token that cancels the lookup.</param>
	/// <returns>The account to sign in, or <see langword="null" /> and the reason the attempt fails.</returns>
	public async Task<(UserRecord? User, LoginFailure? Failure)> FindLoginUserAsync(LoginAttempt attempt,
		CancellationToken cancellationToken)
	{
		var user = await users.GetByNameAsync(attempt.Username, cancellationToken);
		if (user is null || user.IsBasilBot()) return (null, LoginFailure.UnknownUser);
		if (user.Value.DeletedAt is not null) return (null, LoginFailure.AccountDeleted);
		if (!await credentials.IsPasswordAsync(user, attempt.PasswordHash, cancellationToken))
			return (null, LoginFailure.WrongPassword);

		return (user, null);
	}
}
