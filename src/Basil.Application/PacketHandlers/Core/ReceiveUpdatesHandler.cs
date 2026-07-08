using Basil.Application.Sessions;
using Basil.Domain;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>Ported from app/api/domains/cho.py's ReceiveUpdates.</summary>
public sealed class ReceiveUpdatesHandler : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.ReceiveUpdates;

    public bool AllowedWhenRestricted => true;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var value = reader.ReadI32();
        if (value is < 0 or >= 3) return Task.CompletedTask;

        player.PresenceFilter = (PresenceFilter)value;
        return Task.CompletedTask;
    }
}