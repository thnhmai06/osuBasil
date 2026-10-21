using Basil.Application.Persistence.Users;
using Basil.Application.Persistence.Auth;
using Basil.Application.Persistence.Common;
using Basil.Application.Persistence.Content;
using Basil.Application.Persistence.Users;
using Basil.Domain;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Capabilities.Users;

/// <summary>Applies account rules and persists account credentials.</summary>
internal sealed class AccountService(
	IUserRepository users,
	ICredentialRepository credentials,
	IRestrictionRepository restrictions,
	ISettingsRepository settings)
{
	/// <inheritdoc />
	public async Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt,
		CancellationToken cancellationToken)
	{
		if ((await settings.GetAsync(cancellationToken)).LockedCreation.HasFlag(ServerSettings.CreationLocks.Accounts))
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
	public async Task<bool> VerifyAsync(UserRecord user, Md5 passwordHash, CancellationToken cancellationToken)
	{
		try
		{
			return await credentials.VerifyAsync(new Credentials(user, passwordHash), cancellationToken);
		}
		catch (ArgumentException)
		{
			// The client sent something that is not even a well-formed MD5 digest.
			return false;
		}
	}

	/// <inheritdoc />
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

	/// <inheritdoc />
	public async Task<(UserRecord? User, LoginFailure? Failure)> FindLoginUserAsync(LoginAttempt attempt,
		CancellationToken cancellationToken)
	{
		var user = await users.GetByNameAsync(attempt.Username, cancellationToken);
		if (user is null || BasilBot.Is(user)) return (null, LoginFailure.UnknownUser);
		if (user.Value.DeletedAt is not null) return (null, LoginFailure.AccountDeleted);
		if (!await VerifyAsync(user, attempt.PasswordHash, cancellationToken))
			return (null, LoginFailure.WrongPassword);

		return (user, null);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Restriction>> ListRunningRestrictionsAsync(UserRecord user, DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		return (await restrictions.ListAsync(user, cancellationToken))
			.Where(restriction => restriction.Value.EndsAt is null || restriction.Value.EndsAt > now)
			.ToList();
	}
}