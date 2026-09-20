using Basil.Application.Registry;
using Basil.Application.Unresolved.Sessions;

namespace Basil.Application.Unresolved.Auth;

/// <summary>
///     Authenticates an already-online userSession against query-string credentials.
/// </summary>
/// <remarks>
///     The web endpoints that use this service authenticate via a query-string username and
///     password MD5 rather than a session token, and they never establish a session of their own,
///     so this is not a general login path: the userSession must already hold an online
///     <see cref="UserSession" />. Password verification is delegated to
///     <see cref="CredentialVerifier" />, so repeat checks against the same account's hash cost
///     almost nothing.
/// </remarks>
public sealed class AuthenticationService(
	ISessionRegistry<GameSession> sessionRegistry,
	CredentialVerifier credentialVerifier,
	ILogger<AuthenticationService> logger)
{
	/// <summary>
	///     Verifies the supplied password MD5 against the stored hash of the user currently online
	///     under <paramref name="username" />.
	/// </summary>
	/// <param name="username">The name of the userSession to authenticate.</param>
	/// <param name="passwordMd5">The hex-encoded MD5 digest of the userSession's password.</param>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	/// <returns>
	///     The online <see cref="GameSession" /> when the userSession is online with a real osu! client
	///     and the password verifies; otherwise, <see langword="null" />.
	/// </returns>
	public async Task<GameSession?> AuthenticateOnlinePlayerAsync(
		string username, string passwordMd5, CancellationToken cancellationToken = default)
	{
		var session = sessionRegistry.GetByName(username);
		if (session is null)
		{
			logger.LogDebug("Online-userSession authentication failed: Username={Username} (not online)", username);
			return null;
		}

		if (await credentialVerifier.VerifyPasswordAsync(session.Id, passwordMd5, cancellationToken))
			return session;

		logger.LogDebug("Online-userSession authentication failed: Username={Username} (bad password or no hash)",
			username);
		return null;
	}
}