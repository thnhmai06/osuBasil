using System.Net;
using Basil.Application.Users;
using Basil.Domain.Auth;
using Basil.Domain.Client;

namespace Basil.Application.Sessions;

/// <summary>Authenticates logins and opens and closes their connections.</summary>
public sealed class Gateway(
	IUserRepository usersRepository,
	ICredentialRepository credentials,
	UserRegistry usersRegistry,
	TimeProvider time)
{
	/// <summary>A logout an osu! client sends this soon after login is ignored.</summary>
	public static readonly TimeSpan IgnoreLogoutWithin = TimeSpan.FromSeconds(1);

	/// <summary>Authenticates a login attempt and, on success, opens a connection of the requested kind.</summary>
	/// <param name="attempt">The claimed username and password.</param>
	/// <param name="type">The kind of client logging in; not <see cref="ConnectionType.Bot" />.</param>
	/// <param name="ip">The IP address the login came from.</param>
	/// <param name="client">The osu! client that logged in, or <see langword="null" /> for IRC.</param>
	/// <param name="utcOffset">The client's UTC offset reported at login; used only by osu! game clients.</param>
	/// <param name="cancellationToken">A token that cancels the login.</param>
	/// <returns>The login's outcome: the new connection on success, or the reason it failed.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="type" /> is <see cref="ConnectionType.Bot" /> or not a
	///     defined value.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="client" /> is <see langword="null" /> for an osu! or
	///     osu!tourney login.
	/// </exception>
	public async Task<LoginResult> ConnectAsync(LoginAttempt attempt, ConnectionType type, IPAddress ip,
		ClientInfo? client, int utcOffset, CancellationToken cancellationToken = default)
	{
		if (type is ConnectionType.Bot)
			throw new ArgumentOutOfRangeException(nameof(type), type,
				"Only client connections log in through the gateway.");
		if (type is ConnectionType.Bancho or ConnectionType.Tourney && client is null)
			throw new ArgumentNullException(nameof(client), "An osu! client login must report its client.");

		var user = await usersRepository.GetByNameAsync(attempt.Username, cancellationToken);
		if (user is null) return LoginResult.Fail(LoginFailure.UnknownUser);
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
			ConnectionType.Bancho => new BanchoConnection(login, utcOffset, time),
			ConnectionType.Tourney => new TourneyConnection(login),
			ConnectionType.Irc => new IrcConnection(login),
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown connection type.")
		};

		return usersRegistry.OpenConnection(connection) is { } failure
			? LoginResult.Fail(failure)
			: LoginResult.Success(connection);
	}

	/// <summary>Closes a connection.</summary>
	/// <param name="connection">The connection to close.</param>
	/// <param name="reason">Why the connection is closed.</param>
	/// <remarks>
	///     An osu! client's logout within <see cref="IgnoreLogoutWithin" /> of login is ignored: the osu! client sends
	///     one right after logging in.
	/// </remarks>
	public void Disconnect(Connection connection, ConnectionCloseReason reason)
	{
		if (reason is ConnectionCloseReason.LoggedOut && connection is BanchoConnection &&
		    time.GetUtcNow() - connection.Login.Timestamp < IgnoreLogoutWithin)
			return;

		usersRegistry.CloseConnection(connection, reason);
	}
}