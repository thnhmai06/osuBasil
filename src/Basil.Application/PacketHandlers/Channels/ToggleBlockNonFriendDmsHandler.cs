using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Channels;

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