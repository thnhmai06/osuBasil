using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Domain;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.PacketHandlers.Core;

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