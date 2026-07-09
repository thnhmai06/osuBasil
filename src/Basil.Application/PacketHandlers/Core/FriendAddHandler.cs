using Basil.Application.Abstractions.Social;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>Ported from app/api/domains/cho.py's AddFriend. Body is the target user's id (i32).</summary>
public sealed class FriendAddHandler(IRelationshipRepository relationships) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.FriendAdd;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var targetId = reader.ReadI32();
        if (targetId == player.Id) return;

        if (await relationships.FetchOneAsync(player.Id, targetId) is null)
            await relationships.CreateAsync(player.Id, targetId, RelationshipType.Friend);
    }
}
