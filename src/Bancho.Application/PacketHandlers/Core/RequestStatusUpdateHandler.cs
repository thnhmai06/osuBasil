using Bancho.Application.Sessions;
using Bancho.Protocol;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Core;

/// <summary>Ported from app/api/domains/cho.py's StatsUpdateRequest.</summary>
public sealed class RequestStatusUpdateHandler : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.RequestStatusUpdate;

    public bool AllowedWhenRestricted => true;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        player.Enqueue(PacketBuilders.BuildUserStats(player));
        return Task.CompletedTask;
    }
}
