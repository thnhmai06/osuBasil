using System.Net;
using Basil.Application.Contracts.Sessions;
using Basil.Application.Sessions;
using Basil.Domain.Client;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Contracts.Users;

/// <summary>Authenticates users and creates accounts.</summary>
public interface IAuthService
{
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
	/// <remarks>
	///     BasilBot can never log in, whatever password is stored for it. A successful login is recorded in the login history.
	/// </remarks>
	Task<LoginResult> LoginAsync(LoginAttempt attempt, ConnectionType type, IPAddress ip, ClientInfo? client, int utcOffset, CancellationToken cancellationToken = default);

	/// <summary>Checks a registration attempt without creating the account.</summary>
	/// <param name="attempt">The registration attempt to check.</param>
	/// <param name="cancellationToken">A token that cancels the check.</param>
	/// <returns><see langword="null" /> when the attempt would succeed; otherwise, why it would fail.</returns>
	/// <remarks>
	///     While an administrator key is set, the attempt must carry a matching one.
	/// </remarks>
	Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt, CancellationToken cancellationToken = default);

	/// <summary>Creates the account a registration attempt asks for.</summary>
	/// <param name="attempt">The registration attempt.</param>
	/// <param name="cancellationToken">A token that cancels the registration.</param>
	/// <returns>The new user, or why the registration failed.</returns>
	/// <remarks>
	///     While an administrator key is set, the attempt must carry a matching one.
	/// </remarks>
	Task<(User? User, RegistrationFailure? Failure)> RegisterAsync(RegisterAttempt attempt, CancellationToken cancellationToken = default);

	/// <summary>Creates an account on behalf of an administrator, without an administrator key.</summary>
	/// <param name="data">The account data.</param>
	/// <param name="passwordHash">The MD5 digest of the password.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The new user, or why the account could not be created (<see cref="RegistrationFailure.NameTaken" />).</returns>
	Task<(User? User, RegistrationFailure? Failure)> CreateAccountAsync(UserData data, Md5 passwordHash, CancellationToken cancellationToken = default);
}