using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's ReceiveUpdates.</summary>
public sealed class ReceiveUpdatesHandler : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.ReceiveUpdates;

    public bool AllowedWhenRestricted => true;

    public void Handle(PlayerSession player, BanchoPacketReader reader)
    {
        var value = reader.ReadI32();
        if (value is < 0 or >= 3)
        {
            return;
        }

        player.PresenceFilter = (PresenceFilter)value;
    }
}
