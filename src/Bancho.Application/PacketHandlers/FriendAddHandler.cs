using Bancho.Application.Abstractions;
using Bancho.Application.Sessions;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's FriendAdd.</summary>
public sealed class FriendAddHandler(IPlayerSessionRegistry sessionRegistry, IRelationshipRepository relationships) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.FriendAdd;

    public bool AllowedWhenRestricted => true;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var userId = reader.ReadI32();

        var target = sessionRegistry.GetById(userId);
        if (target is null || target.IsBotClient)
        {
            return;
        }

        var existing = await relationships.FetchOneAsync(player.Id, target.Id);
        if (existing?.Type == RelationshipType.Block)
        {
            await relationships.DeleteAsync(player.Id, target.Id);
        }

        await relationships.CreateAsync(player.Id, target.Id, RelationshipType.Friend);
    }
}
