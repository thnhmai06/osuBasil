using System.Net;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Client;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Contracts.Users;

/// <summary>Authenticates users and manages accounts and passwords.</summary>
public interface IAuthService
{
	/// <summary>Authenticates a login attempt and, on success, opens a connection of the requested kind.</summary>
	/// <param name="attempt">The claimed username and password.</param>
	/// <param name="type">The kind of client logging in.</param>
	/// <param name="ip">The IP address the login came from.</param>
	/// <param name="client">The osu! client that logged in, or <see langword="null" /> for IRC and the HTTP API.</param>
	/// <param name="utcOffset">The client's UTC offset reported at login; used only by osu! game clients.</param>
	/// <param name="cancellationToken">A token that cancels the login.</param>
	/// <returns>The login's outcome: the new connection on success, or the reason it failed.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="type" /> is not a defined value.</exception>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="client" /> is <see langword="null" /> for an osu! or
	///     osu!tourney login.
	/// </exception>
	/// <remarks>
	///     An osu!tourney login needs <see cref="Permissions.TournamentObserveRooms" /> in effect. A successful login is
	///     recorded in the login history.
	/// </remarks>
	Task<LoginResult> LoginAsync(LoginAttempt attempt, ConnectionType type, IPAddress ip, ClientInfo? client,
		int utcOffset, CancellationToken cancellationToken = default);

	/// <summary>Checks a registration attempt without creating the account.</summary>
	/// <param name="attempt">The registration attempt to check.</param>
	/// <param name="cancellationToken">A token that cancels the check.</param>
	/// <returns><see langword="null" /> when the attempt would succeed; otherwise, why it would fail.</returns>
	/// <remarks>
	///     While an administrator key is set, the attempt must carry a matching one.
	/// </remarks>
	Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt,
		CancellationToken cancellationToken = default);

	/// <summary>Creates the account a registration attempt asks for.</summary>
	/// <param name="attempt">The registration attempt.</param>
	/// <param name="cancellationToken">A token that cancels the registration.</param>
	/// <returns>The new user, or why the registration failed.</returns>
	/// <remarks>
	///     While an administrator key is set, the attempt must carry a matching one.
	/// </remarks>
	Task<(User? User, RegistrationFailure? Failure)> RegisterAsync(RegisterAttempt attempt,
		CancellationToken cancellationToken = default);

	/// <summary>Creates an account on behalf of a user who manages accounts, without an administrator key.</summary>
	/// <param name="by">
	///     The connection acting; its user needs <see cref="Permissions.OwnerManageAccounts" /> and every permission
	///     the new account is granted.
	/// </param>
	/// <param name="data">The account data.</param>
	/// <param name="passwordHash">The MD5 digest of the password.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The new user, or why the account could not be created (<see cref="RegistrationFailure.NotAuthorized" /> or
	///     <see cref="RegistrationFailure.NameTaken" />).
	/// </returns>
	Task<(User? User, RegistrationFailure? Failure)> CreateAccountAsync(Connection by, UserData data,
		Md5 passwordHash, CancellationToken cancellationToken = default);

	/// <summary>Replaces a user's password and ends every session they have.</summary>
	/// <param name="by">
	///     The connection acting: the user themselves, who must give the current password, or a user with
	///     <see cref="Permissions.OwnerManageAccounts" /> who outranks them.
	/// </param>
	/// <param name="user">The user whose password is replaced.</param>
	/// <param name="newPasswordHash">The MD5 digest of the new password.</param>
	/// <param name="currentPasswordHash">The MD5 digest of the current password, needed when users change their own.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome.</returns>
	/// <remarks>
	///     Every connection of the user, the caller's own included, is closed as
	///     <see cref="Sessions.ConnectionCloseReason.CredentialsChanged" />.
	/// </remarks>
	Task<PasswordChangeResult> ChangePasswordAsync(Connection by, User user, Md5 newPasswordHash,
		Md5? currentPasswordHash, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of replacing a password.</summary>
public enum PasswordChangeResult : byte
{
	/// <summary>The password was replaced.</summary>
	Changed,

	/// <summary>The caller may not change this user's password.</summary>
	NotAuthorized,

	/// <summary>The current password given by the user is wrong.</summary>
	WrongPassword
}