using Bancho.Application.Abstractions;
using Bancho.Application.Sessions;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Core;

/// <summary>
///     Ported from app/api/domains/cho.py's Logout — the 1-second login-grace-period check plus the shared
///     PlayerLogoutService cleanup.
/// </summary>
public sealed class LogoutHandler(PlayerLogoutService logoutService, IClock clock) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.Logout;

    public bool AllowedWhenRestricted => true;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        reader.ReadI32(); // reserved

        // osu! has a weird tendency to log out immediately after login (300-800ms observed) —
        // block any logout request within 1 second from login.
        if (clock.UtcNow.ToUnixTimeSeconds() - player.LoginTime < 1) return Task.CompletedTask;

        logoutService.Logout(player);

        return Task.CompletedTask;
    }
}