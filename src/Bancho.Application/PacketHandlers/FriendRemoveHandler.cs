using Bancho.Application.Abstractions;
using Bancho.Application.Sessions;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's FriendRemove.</summary>
public sealed class FriendRemoveHandler(IPlayerSessionRegistry sessionRegistry, IRelationshipRepository relationships) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.FriendRemove;

    public bool AllowedWhenRestricted => true;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var userId = reader.ReadI32();

        var target = sessionRegistry.GetById(userId);
        if (target is null || target.IsBotClient)
        {
            return;
        }

        await relationships.DeleteAsync(player.Id, target.Id);
    }
}
