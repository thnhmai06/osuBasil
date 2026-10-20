using System.Net;
using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Users;
using Basil.Application.Services.Sessions;
using Basil.Application.Storage.Auth;
using Basil.Application.Storage.Common;
using Basil.Application.Storage.Content;
using Basil.Application.Storage.Users;
using Basil.Domain;
using Basil.Domain.Auth;
using Basil.Domain.Bancho;
using Basil.Domain.Common;
using Basil.Domain.Content;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Users;

/// <summary>Authenticates users and manages accounts and passwords.</summary>
internal sealed class AuthService(
	IUserRepository users,
	ICredentialRepository credentials,
	ILoginRepository logins,
	IRestrictionRepository restrictions,
	ISettingsRepository settings,
	SessionConnectionsService connections,
	SessionContext context,
	TimeProvider time) : IAuthService
{
	/// <inheritdoc />
	public async Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt,
		CancellationToken cancellationToken = default)
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

		if (await users.GetByNameAsync(attempt.Username, cancellationToken) is not null)
			return RegistrationFailure.NameTaken;

		return null;
	}

	/// <inheritdoc />
	public async Task<LoginResult> LoginAsync(LoginAttempt attempt, ConnectionType type, IPAddress ip,
		BanchoClient? client, int utcOffset, CancellationToken cancellationToken = default)
	{
		type.ThrowIfUndefined();
		switch (type)
		{
			case ConnectionType.Bot:
				throw new ArgumentException("The bot's connection cannot be requested.", nameof(type));
			case ConnectionType.Bancho or ConnectionType.Tourney when client is null:
				throw new ArgumentNullException(nameof(client), "An osu! client login must report its client.");
		}

		var user = await users.GetByNameAsync(attempt.Username, cancellationToken);
		if (user is null || BasilBot.Is(user)) return LoginResult.Fail(LoginFailure.UnknownUser);
		if (user.Value.DeletedAt is not null) return LoginResult.Fail(LoginFailure.AccountDeleted);
		if (!await VerifyAsync(user, attempt.PasswordHash, cancellationToken))
			return LoginResult.Fail(LoginFailure.WrongPassword);

		var now = time.GetUtcNow();
		var running = (await restrictions.ListAsync(user, cancellationToken))
			.Where(restriction => restriction.Value.EndsAt is null || restriction.Value.EndsAt > now)
			.ToList();
		var login = new Login { User = user, Ip = ip, Client = client, Timestamp = now };
		var token = SessionConnectionsService.NewToken();
		Connection connection = type switch
		{
			ConnectionType.Bancho => new BanchoConnection(login, token, utcOffset, context),
			ConnectionType.Tourney => new TourneyConnection(login, token),
			ConnectionType.Irc => new IrcConnection(login, token),
			_ => new ApiConnection(login, token)
		};

		var failure = connections.Open(connection, running);
		if (failure is not null) return LoginResult.Fail(failure.Value);

		await logins.RecordAsync(login, cancellationToken);
		return LoginResult.Success(connection);
	}

	/// <inheritdoc />
	public async Task<(UserRecord? User, RegistrationFailure? Failure)> RegisterAsync(RegisterAttempt attempt,
		CancellationToken cancellationToken = default)
	{
		var failure = await CheckRegistrationAsync(attempt, cancellationToken);
		if (failure is not null) return (null, failure);

		return await CreateCoreAsync(new UserData { Name = attempt.Username }, attempt.PasswordHash, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAccountAsync(Connection by, UserData data,
		Md5 passwordHash, CancellationToken cancellationToken = default)
	{
		if (!PermissionRules.Allows(by, Permissions.UserManage, time.GetUtcNow()) ||
		    !PermissionRules.Granted(by).Allows(data.Permissions))
			return (null, RegistrationFailure.NotAuthorized);
		if (await users.GetByNameAsync(data.Name, cancellationToken) is not null)
			return (null, RegistrationFailure.NameTaken);

		return await CreateCoreAsync(data, passwordHash, cancellationToken);
	}

	private async Task<bool> VerifyAsync(UserRecord user, Md5 passwordHash, CancellationToken cancellationToken)
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

	private async Task<(UserRecord? User, RegistrationFailure? Failure)> CreateCoreAsync(UserData data,
		Md5 passwordHash,
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
}