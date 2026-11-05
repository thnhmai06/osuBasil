using Basil.Application.Authentication.Persistence;
using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Ports;
using Basil.Domain.Common;
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

	/// <inheritdoc />
	public async Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAsync(UserData data, Md5 passwordHash,
		UserPermissions? permissions, CancellationToken cancellationToken)
	{
		data.Permissions = permissions ?? (await settings.GetAsync(cancellationToken)).Accounts.DefaultPermissions;
		UserRecord user;
		try
		{
			user = await users.CreateAsync(data, cancellationToken);
		}
		catch (AlreadyExistsException)
		{
			return (null, RegistrationFailure.NameTaken);
		}

		try
		{
			await credentials.CreateOrUpdateAsync(new Credentials(user, passwordHash), cancellationToken);
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
		CancellationToken cancellationToken)
	{
		var user = await users.GetByNameAsync(attempt.Username, cancellationToken);
		if (user is null || user.IsBasilBot()) return (null, LoginFailure.UnknownUser);
		if (user.Value.DeletedAt is not null) return (null, LoginFailure.AccountDeleted);
		if (!await credentials.IsPasswordAsync(user, attempt.PasswordHash, cancellationToken))
			return (null, LoginFailure.WrongPassword);

		return (user, null);
	}

	private async Task DiscardAsync(UserRecord user)
	{
		try
		{
			await users.DiscardAsync(user, CancellationToken.None);
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Could not discard user {UserId} whose credential was not stored", user.Id);
		}
	}
}