using Basil.Application.Abstractions.Social;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>Ported from app/api/domains/cho.py's RemoveFriend. Body is the target user's id (i32).</summary>
public sealed class FriendRemoveHandler(IRelationshipRepository relationships) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.FriendRemove;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var targetId = reader.ReadI32();
        var relationship = await relationships.FetchOneAsync(player.Id, targetId);
        if (relationship?.Type == RelationshipType.Friend)
            await relationships.DeleteAsync(player.Id, targetId);
    }
}
