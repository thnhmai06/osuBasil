using System.Net;
using Basil.Application.Contracts.Users;
using Basil.Application.Services.Sessions;
using Basil.Application.Storage.Sessions;
using Basil.Application.Storage.Users;
using Basil.Domain.Auth;
using Basil.Domain.Client;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Services.Users;

/// <summary>Authenticates users and creates accounts.</summary>
internal sealed class AuthService(
	IUserRepository users,
	ICredentialRepository credentials,
	ILoginRepository logins,
	SessionService sessions,
	TimeProvider time) : IAuthService
{
	/// <inheritdoc />
	public async Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt,
		CancellationToken cancellationToken = default)
	{
		if (await credentials.GetAdminKeyUpdatedAtAsync(cancellationToken) is not null &&
		    (attempt.AdminKey is not { } key || !await credentials.VerifyAdminKeyAsync(key, cancellationToken)))
			return RegistrationFailure.WrongAdminKey;

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
		if (type is ConnectionType.Bot)
			throw new ArgumentOutOfRangeException(nameof(type), type, "Only client connections log in.");
		if (type is ConnectionType.Bancho or ConnectionType.Tourney && client is null)
			throw new ArgumentNullException(nameof(client), "An osu! client login must report its client.");

		var user = await users.GetByNameAsync(attempt.Username, cancellationToken);
		if (user is null) return LoginResult.Fail(LoginFailure.UnknownUser);
		if (user.Id == SystemUserIds.BasilBot) return LoginResult.Fail(LoginFailure.WrongPassword);
		if (user.Value.DeletedAt is not null) return LoginResult.Fail(LoginFailure.AccountDeleted);

		bool verified;
		try
		{
			verified = await credentials.VerifyAsync(new Credentials(user, attempt.PasswordHash), cancellationToken);
		}
		catch (ArgumentException)
		{
			// The client sent something that is not even a well-formed MD5 digest.
			verified = false;
		}

		if (!verified) return LoginResult.Fail(LoginFailure.WrongPassword);

		var login = new Login { User = user, Ip = ip, Client = client, Timestamp = time.GetUtcNow() };
		Connection connection = type switch
		{
			ConnectionType.Bancho => new BanchoConnection(login, utcOffset),
			ConnectionType.Tourney => new TourneyConnection(login),
			ConnectionType.Irc => new IrcConnection(login),
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown connection type.")
		};

		var failure = sessions.Open(connection);
		if (failure is not null) return LoginResult.Fail(failure.Value);

		await logins.CreateAsync(login, cancellationToken);
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
	public async Task<(User? User, RegistrationFailure? Failure)> CreateAccountAsync(UserData data, Md5 passwordHash,
		CancellationToken cancellationToken = default)
	{
		if (await users.GetByNameAsync(data.Name, cancellationToken) is not null)
			return (null, RegistrationFailure.NameTaken);

		return await CreateCoreAsync(data, passwordHash, cancellationToken);
	}

	private async Task<(User? User, RegistrationFailure? Failure)> CreateCoreAsync(UserData data, Md5 passwordHash,
		CancellationToken cancellationToken)
	{
		var user = await users.CreateAsync(data, cancellationToken);
		await credentials.CreateOrUpdateAsync(new Credentials(user, passwordHash), cancellationToken);
		return (user, null);
	}
}