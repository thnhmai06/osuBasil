using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>
///     Ported from app/api/domains/cho.py's Logout — the 1-second login-grace-period check plus the shared
///     PlayerLogoutService cleanup.
/// </summary>
public sealed class LogoutHandler(PlayerLogoutService logoutService) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.Logout;

    public bool AllowedWhenRestricted => true;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        reader.ReadI32(); // reserved

        // osu! has a weird tendency to log out immediately after login (300-800ms observed) —
        // block any logout request within 1 second from login.
        if (DateTimeOffset.UtcNow - player.LoginTime < TimeSpan.FromSeconds(1)) return;

        await logoutService.LogoutAsync(player);
    }
}