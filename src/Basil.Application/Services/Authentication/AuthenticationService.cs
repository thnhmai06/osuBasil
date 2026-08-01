using System.Text;
using Basil.Application.Abstractions.Users;
using Basil.Application.Sessions;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Authentication;

/// <summary>
///     Authenticates an already-online player against query-string credentials.
/// </summary>
/// <remarks>
///     The web endpoints that use this service authenticate via a query-string username and
///     password MD5 rather than a session token, and they never establish a session of their own,
///     so this is not a general login path: the player must already hold an online
///     <see cref="PlayerSession" />. Password verification reuses <see cref="IPasswordHasher" />, so
///     repeat checks against the same account's hash cost almost nothing.
/// </remarks>
public sealed class AuthenticationService(
	IPlayerSessionRegistry sessionRegistry,
	IUserRepository users,
	IPasswordHasher passwordHasher,
	ILogger<AuthenticationService> logger)
{
	/// <summary>
	///     Verifies the supplied password MD5 against the stored hash of the user currently online
	///     under <paramref name="username" />.
	/// </summary>
	/// <param name="username">The name of the player to authenticate.</param>
	/// <param name="passwordMd5">The hex-encoded MD5 digest of the player's password.</param>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	/// <returns>
	///     The online <see cref="PlayerSession" /> when the player is online and the password verifies;
	///     otherwise, <see langword="null" />.
	/// </returns>
	public async Task<PlayerSession?> AuthenticateOnlinePlayerAsync(
		string username, string passwordMd5, CancellationToken cancellationToken = default)
	{
		var session = sessionRegistry.GetByName(username);
		if (session is null)
		{
			logger.LogDebug("Online-player authentication failed: Username={Username} (not online)", username);
			return null;
		}

		var passwordHash = await users.FetchPasswordHashAsync(session.Id, cancellationToken);
		if (passwordHash is null)
		{
			logger.LogDebug("Online-player authentication failed: Username={Username} (no password hash)", username);
			return null;
		}

		if (passwordHasher.Verify(Encoding.UTF8.GetBytes(passwordMd5), passwordHash)) return session;

		logger.LogDebug("Online-player authentication failed: Username={Username} (bad password)", username);
		return null;
	}
}