using Basil.Application.Sessions;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>
///     Handles the Logout packet, which disconnects the player from the server.
/// </summary>
/// <remarks>
///     The body's leading 32-bit integer is reserved and discarded. A logout request sent within one
///     second of login is ignored and logged at debug level, so that the client's tendency to send a
///     logout immediately after a successful login does not disconnect a freshly logged-in player.
///     Any other request is handed to <see cref="PlayerLogoutService.LogoutAsync" />, which leaves
///     the player's match, removes spectators, parts every joined channel, drops the session from
///     the registry, and notifies the remaining online players.
/// </remarks>
public sealed class LogoutHandler(PlayerLogoutService logoutService, ILogger<LogoutHandler> logger)
	: IBanchoPacketHandler
{
	/// <summary>The <see cref="ClientPackets.Logout" /> packet type.</summary>
	public ClientPackets PacketId => ClientPackets.Logout;

	/// <summary>Restricted players may log out, so this handler is always available.</summary>
	public bool AllowedWhenRestricted => true;

	/// <summary>Reads the reserved field and runs the logout flow once the grace period has passed.</summary>
	/// <param name="player">The player session that is logging out.</param>
	/// <param name="reader">The packet reader positioned at the Logout body.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A task that completes once the logout is handled or the request is ignored.</returns>
	public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		reader.ReadI32(); // reserved

		// osu! has a weird tendency to log out immediately after login (300-800ms observed) —
		// block any logout request within 1 second from login.
		if (DateTimeOffset.UtcNow - player.LoginTime < TimeSpan.FromSeconds(1))
		{
			logger.LogDebug("Logout within grace period ignored: UserId={UserId}", player.Id);
			return;
		}

		await logoutService.LogoutAsync(player, cancellationToken);
	}
}