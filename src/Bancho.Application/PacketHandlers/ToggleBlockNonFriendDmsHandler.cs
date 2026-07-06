using Bancho.Application.Sessions;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's ToggleBlockingDMs.</summary>
public sealed class ToggleBlockNonFriendDmsHandler : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.ToggleBlockNonFriendDms;

    public bool AllowedWhenRestricted => true;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        player.PmPrivate = reader.ReadI32() == 1;
        return Task.CompletedTask;
    }
}
