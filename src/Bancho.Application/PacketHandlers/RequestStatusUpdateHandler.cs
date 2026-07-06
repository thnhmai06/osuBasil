using Bancho.Application.Sessions;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's StatsUpdateRequest.</summary>
public sealed class RequestStatusUpdateHandler : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.RequestStatusUpdate;

    public bool AllowedWhenRestricted => true;

    public void Handle(PlayerSession player, BanchoPacketReader reader) =>
        player.Enqueue(PacketBuilders.BuildUserStats(player));
}
