using Bancho.Application.Abstractions;
using Bancho.Application.Sessions;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>
/// Ported from app/api/domains/cho.py's Logout + Player.logout, scoped to what Phase 3 needs
/// (match/spectator/channel membership cleanup is added once those subsystems exist).
/// </summary>
public sealed class LogoutHandler(IPlayerSessionRegistry sessionRegistry, IClock clock) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.Logout;

    public bool AllowedWhenRestricted => true;

    public void Handle(PlayerSession player, BanchoPacketReader reader)
    {
        reader.ReadI32(); // reserved

        // osu! has a weird tendency to log out immediately after login (300-800ms observed) —
        // block any logout request within 1 second from login.
        if (clock.UtcNow.ToUnixTimeSeconds() - player.LoginTime < 1)
        {
            return;
        }

        sessionRegistry.Remove(player);

        if (!player.Restricted)
        {
            foreach (var other in sessionRegistry.All)
            {
                other.Enqueue(ServerPacketWriter.Logout(player.Id));
            }
        }
    }
}
