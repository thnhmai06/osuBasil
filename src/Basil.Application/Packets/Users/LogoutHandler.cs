using Basil.Application.Sessions;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Packets.Users;

/// <summary>
///     Handles the Logout packet, which disconnects the userSession from the server.
/// </summary>
/// <remarks>
///     The body's leading 32-bit integer is reserved and discarded. A logout request sent within one
///     second of login is ignored and logged at debug level, so that the client's tendency to send a
///     logout immediately after a successful login does not disconnect a freshly logged-in userSession.
///     Any other request is handed to <see cref="PlayerLogoutService.LogoutAsync" />, which leaves
///     the userSession's match, removes spectators, parts every joined channel, drops the session from
///     the registry, and notifies the remaining online players.
/// </remarks>
public sealed class LogoutHandler(PlayerLogoutService logoutService, ILogger<LogoutHandler> logger)
	: IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.Logout;

	public bool AllowedWhenRestricted => true;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		reader.ReadI32(); // reserved

		// osu! tends to log out immediately after login (300-800ms observed) —
		// block any logout request within 1 second from login. (that's so weird lol)
		if (DateTimeOffset.UtcNow - gameSession.LoginTime < TimeSpan.FromSeconds(1))
		{
			logger.LogDebug("Logout within grace period ignored: UserId={UserId}", gameSession.Id);
			return;
		}

		await logoutService.LogoutAsync(gameSession, cancellationToken);
	}
}