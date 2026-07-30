using Basil.Application.Sessions;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>
///     Ported from app/api/domains/cho.py's Logout — the 1-second login-grace-period check plus the shared
///     PlayerLogoutService cleanup.
/// </summary>
public sealed class LogoutHandler(PlayerLogoutService logoutService, ILogger<LogoutHandler> logger)
	: IBanchoPacketHandler
{
	public ClientPackets PacketId => ClientPackets.Logout;

	public bool AllowedWhenRestricted => true;

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