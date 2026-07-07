using Bancho.Application.Abstractions.Social;
using Bancho.Application.Abstractions.Users;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Application.Sessions;
using Bancho.Protocol;
using Bancho.Protocol.Packets;
using Action = Bancho.Domain.Action;

namespace Bancho.Application.PacketHandlers.Channels;

/// <summary>
///     Ported from app/api/domains/cho.py's SendMessage (private). BanchoBot's special-cased routing is dropped along
///     with the bot itself.
/// </summary>
public sealed class SendPrivateMessageHandler(
    IPlayerSessionRegistry sessionRegistry,
    IUserRepository users,
    IRelationshipRepository relationships,
    IMailRepository mail) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.SendPrivateMessage;

    public bool AllowedWhenRestricted => true;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var message = reader.ReadMessage();

        if (player.Silenced) return;

        var target = sessionRegistry.GetByName(message.Recipient);

        await DeliverToRealTargetAsync(player, message, target);
    }

    private async Task DeliverToRealTargetAsync(PlayerSession player, BanchoMessage message, PlayerSession? target)
    {
        int targetId;
        if (target is not null)
        {
            targetId = target.Id;
        }
        else
        {
            var targetUser = await users.FetchByNameAsync(message.Recipient);
            if (targetUser is null) return;

            targetId = targetUser.Id;
        }

        var relationship = await relationships.FetchOneAsync(targetId, player.Id);
        if (relationship?.Type == RelationshipType.Block)
        {
            player.Enqueue(ServerPacketWriter.UserDmBlocked(message.Recipient));
            return;
        }

        if (target is not null)
        {
            if (target.PmPrivate && relationship?.Type != RelationshipType.Friend)
            {
                player.Enqueue(ServerPacketWriter.UserDmBlocked(message.Recipient));
                return;
            }

            if (target.Silenced)
            {
                player.Enqueue(ServerPacketWriter.TargetSilenced(message.Recipient));
                return;
            }

            target.Enqueue(ServerPacketWriter.SendMessage(player.Name, message.Text, message.Recipient, player.Id));

            if (target.Status.Action == Action.Afk && target.AwayMessage is { } awayMessage)
                player.Enqueue(ServerPacketWriter.SendMessage(target.Name, awayMessage, player.Name, target.Id));
        }

        await mail.CreateAsync(player.Id, targetId, message.Text);
    }
}