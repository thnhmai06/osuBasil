using System.Net;
using Basil.Application.Services.Contracts.Users;
using Basil.Application.Services.Implementations.Sessions;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Auth;
using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Content;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Auth;
using Basil.Domain.Client;
using Basil.Domain.Content;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Services.Implementations.Users;

/// <summary>Authenticates users and manages accounts and passwords.</summary>
internal sealed class AuthService(
	IUserRepository users,
	ICredentialRepository credentials,
	ILoginRepository logins,
	IRestrictionRepository restrictions,
	ISettingsRepository settings,
	SessionService sessions,
	TimeProvider time) : IAuthService
{
	/// <inheritdoc />
	public async Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt,
		CancellationToken cancellationToken = default)
	{
		if ((await settings.GetAsync(cancellationToken)).LockedCreation.HasFlag(CreationLocks.Accounts))
			return RegistrationFailure.Locked;

		try
		{
			_ = new UserData { Name = attempt.Username };
		}
		catch (ArgumentException)
		{
			return RegistrationFailure.InvalidName;
		}

		if (await users.GetByNameAsync(attempt.Username, cancellationToken) is not null)
			return RegistrationFailure.NameTaken;

		return null;
	}

	/// <inheritdoc />
	public async Task<LoginResult> LoginAsync(LoginAttempt attempt, ConnectionType type, IPAddress ip,
		ClientInfo? client, int utcOffset, CancellationToken cancellationToken = default)
	{
		type.ThrowIfUndefined();
		if (type is ConnectionType.Bancho or ConnectionType.Tourney && client is null)
			throw new ArgumentNullException(nameof(client), "An osu! client login must report its client.");

		var user = await users.GetByNameAsync(attempt.Username, cancellationToken);
		if (user is null) return LoginResult.Fail(LoginFailure.UnknownUser);
		if (user.Value.DeletedAt is not null) return LoginResult.Fail(LoginFailure.AccountDeleted);
		if (!await VerifyAsync(user, attempt.PasswordHash, cancellationToken))
			return LoginResult.Fail(LoginFailure.WrongPassword);

		var now = time.GetUtcNow();
		var running = (await restrictions.ListAsync(user, cancellationToken))
			.Where(restriction => restriction.Value.EndsAt is null || restriction.Value.EndsAt > now)
			.ToList();
		var login = new Login { User = user, Ip = ip, Client = client, Timestamp = now };
		var token = SessionService.NewToken();
		Connection connection = type switch
		{
			ConnectionType.Bancho => new BanchoConnection(login, token, utcOffset),
			ConnectionType.Tourney => new TourneyConnection(login, token),
			ConnectionType.Irc => new IrcConnection(login, token),
			_ => new ApiConnection(login, token)
		};

		var failure = sessions.Open(connection, running);
		if (failure is not null) return LoginResult.Fail(failure.Value);

		await logins.RecordAsync(login, cancellationToken);
		return LoginResult.Success(connection);
	}

	/// <inheritdoc />
	public async Task<(User? User, RegistrationFailure? Failure)> RegisterAsync(RegisterAttempt attempt,
		CancellationToken cancellationToken = default)
	{
		var failure = await CheckRegistrationAsync(attempt, cancellationToken);
		if (failure is not null) return (null, failure);

		return await CreateCoreAsync(new UserData { Name = attempt.Username }, attempt.PasswordHash, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<(User? User, RegistrationFailure? Failure)> CreateAccountAsync(Connection by, UserData data,
		Md5 passwordHash, CancellationToken cancellationToken = default)
	{
		if (!PermissionRules.Allows(by, Permissions.OwnerManageAccounts, time.GetUtcNow()) ||
		    !PermissionRules.Granted(by).Allows(data.Permissions))
			return (null, RegistrationFailure.NotAuthorized);
		if (await users.GetByNameAsync(data.Name, cancellationToken) is not null)
			return (null, RegistrationFailure.NameTaken);

		return await CreateCoreAsync(data, passwordHash, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<PasswordChangeResult> ChangePasswordAsync(Connection by, User user, Md5 newPasswordHash,
		Md5? currentPasswordHash, CancellationToken cancellationToken = default)
	{
		// Acting for a user is not being that user: a delegated connection never takes the owner's path.
		if (by is not DelegatedConnection && by.User.Equals(user))
		{
			if (currentPasswordHash is not { } current || !await VerifyAsync(user, current, cancellationToken))
				return PasswordChangeResult.WrongPassword;
		}
		else if (!PermissionRules.MayActOn(by, user, Permissions.OwnerManageAccounts, time.GetUtcNow()))
		{
			return PasswordChangeResult.NotAuthorized;
		}

		await credentials.CreateOrUpdateAsync(new Credentials(user, newPasswordHash), cancellationToken);
		sessions.CloseAll(user, ConnectionCloseReason.CredentialsChanged);
		return PasswordChangeResult.Changed;
	}

	private async Task<bool> VerifyAsync(User user, Md5 passwordHash, CancellationToken cancellationToken)
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

	private async Task<(User? User, RegistrationFailure? Failure)> CreateCoreAsync(UserData data, Md5 passwordHash,
		CancellationToken cancellationToken)
	{
		User user;
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
}