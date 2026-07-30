using System.Text;
using Basil.Application.Abstractions.Users;
using Basil.Application.Sessions;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Authentication;

/// <summary>
///     Ported from app/services/bancho.py's AuthenticationService.authenticate_online_player —
///     used by osu-web endpoints (getscores, etc.) that authenticate via query-string username +
///     password_md5 rather than a session token. Requires the player to already be an online bancho
///     session (this is not a general login path). The Python source keeps its own bcrypt-hash cache
///     for this; IPasswordHasher.Verify already caches verified (hash -> md5) pairs itself, so reusing
///     it here gets the same near-zero-cost repeat-check behavior for free.
/// </summary>
public sealed class AuthenticationService(
	IPlayerSessionRegistry sessionRegistry,
	IUserRepository users,
	IPasswordHasher passwordHasher,
	ILogger<AuthenticationService> logger)
{
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