using Bancho.Application.Sessions;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>
/// Handles one client-to-server Bancho packet. Ported from app/api/domains/cho.py's
/// BasePacket subclasses — each Python class's `__init__(reader)` + `handle(player)` are
/// merged into a single HandleAsync call here. Async since Phase 4 handlers (mail/relationship/
/// user lookups) need real I/O — sync-over-async here would risk thread-pool starvation under
/// concurrent packet load, which matters most for this server's multiplayer focus.
/// </summary>
public interface IBanchoPacketHandler
{
    ClientPackets PacketId { get; }

    /// <summary>Whether this handler may run for a restricted player. Ported from @register(..., restricted=True).</summary>
    bool AllowedWhenRestricted { get; }

    Task HandleAsync(PlayerSession player, BanchoPacketReader reader);
}
