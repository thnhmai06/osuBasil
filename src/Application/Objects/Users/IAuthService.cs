using System.Net;
using Basil.Application.Objects.Sessions;
using Basil.Domain.Bancho;
using Basil.Domain.Common;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Users;

/// <summary>Authenticates users and manages accounts and passwords.</summary>
public interface IAuthService
{
	/// <summary>Authenticates a login attempt and, on success, opens a connection of the requested kind.</summary>
	/// <param name="attempt">The claimed username and password.</param>
	/// <param name="type">The kind of connection requested.</param>
	/// <param name="ip">The IP address the login came from.</param>
	/// <param name="client">The osu! client that logged in, or <see langword="null" /> for IRC and the HTTP API.</param>
	/// <param name="utcOffset">The client's UTC offset reported at login; used only by osu! game clients.</param>
	/// <param name="cancellationToken">A token that cancels the login.</param>
	/// <returns>The login's outcome: the new connection on success, or the reason it failed.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="type" /> is not a defined value.</exception>
	/// <exception cref="ArgumentException"><paramref name="type" /> is BasilBot's own connection.</exception>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="client" /> is <see langword="null" /> for an osu! or
	///     osu!tourney login.
	/// </exception>
	/// <remarks>
	///     An osu!tourney login needs <see cref="Permissions.RoomObserve" /> in effect. A successful login is
	///     recorded in the login history.
	/// </remarks>
	Task<LoginResult> LoginAsync(LoginAttempt attempt, ConnectionType type, IPAddress ip, BanchoClient? client,
		int utcOffset, CancellationToken cancellationToken = default);

	/// <summary>Checks a registration attempt without creating the account.</summary>
	/// <param name="attempt">The registration attempt to check.</param>
	/// <param name="cancellationToken">A token that cancels the check.</param>
	/// <returns><see langword="null" /> when the attempt would succeed; otherwise, why it would fail.</returns>
	/// <remarks>
	///     Anyone may register a new account of their own, granted the permissions of a new account, unless account
	///     creation is locked (<see cref="CreationLocks.Accounts" />), which refuses it with
	///     <see cref="RegistrationFailure.Locked" />.
	/// </remarks>
	Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt,
		CancellationToken cancellationToken = default);

	/// <summary>Creates the account a registration attempt asks for.</summary>
	/// <param name="attempt">The registration attempt.</param>
	/// <param name="cancellationToken">A token that cancels the registration.</param>
	/// <returns>The new user, or why the registration failed.</returns>
	/// <remarks>
	///     Anyone may register a new account of their own, granted the permissions of a new account, unless account
	///     creation is locked (<see cref="CreationLocks.Accounts" />), which refuses it with
	///     <see cref="RegistrationFailure.Locked" />.
	/// </remarks>
	Task<(UserRecord? User, RegistrationFailure? Failure)> RegisterAsync(RegisterAttempt attempt,
		CancellationToken cancellationToken = default);

	/// <summary>Creates an account on behalf of a user who manages accounts, whether or not account creation is locked.</summary>
	/// <param name="by">
	///     The connection acting; its user needs <see cref="Permissions.UserManage" /> and every permission
	///     the new account is granted.
	/// </param>
	/// <param name="data">The account data.</param>
	/// <param name="passwordHash">The MD5 digest of the password.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The new user, or why the account could not be created (<see cref="RegistrationFailure.NotAuthorized" /> or
	///     <see cref="RegistrationFailure.NameTaken" />).
	/// </returns>
	Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAccountAsync(Connection by, UserData data,
		Md5 passwordHash, CancellationToken cancellationToken = default);
}