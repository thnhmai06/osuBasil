using Bancho.Application.Sessions;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>
/// Handles one client-to-server Bancho packet. Ported from app/api/domains/cho.py's
/// BasePacket subclasses — each Python class's `__init__(reader)` + `handle(player)` are
/// merged into a single Handle call here, since none of Phase 3's handlers need to defer
/// parsing past construction.
/// </summary>
public interface IBanchoPacketHandler
{
    ClientPackets PacketId { get; }

    /// <summary>Whether this handler may run for a restricted player. Ported from @register(..., restricted=True).</summary>
    bool AllowedWhenRestricted { get; }

    void Handle(PlayerSession player, BanchoPacketReader reader);
}
