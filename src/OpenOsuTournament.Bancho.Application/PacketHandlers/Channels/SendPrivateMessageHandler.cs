using OpenOsuTournament.Bancho.Application.Abstractions.Social;
using OpenOsuTournament.Bancho.Application.Abstractions.Users;
using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.UseCases.Bot;
using OpenOsuTournament.Bancho.Protocol;
using OpenOsuTournament.Bancho.Protocol.Packets;
using Action = OpenOsuTournament.Bancho.Domain.Action;

namespace OpenOsuTournament.Bancho.Application.PacketHandlers.Channels;

/// <summary>
///     Ported from app/api/domains/cho.py's SendMessage (private). BanchoBot's special-cased routing
///     is re-added (see docs/scope-decisions.md) as a command-dispatch shortcut only — DMs to the bot
///     never go through the normal deliver/mail path (nothing ever reads the bot's queue), and `!mp`
///     is intentionally never usable this way: matchScope is always null for PMs, since a private
///     message is never a match's own chat channel (matches bancho.py's ensure_match check).
/// </summary>
public sealed class SendPrivateMessageHandler(
    IPlayerSessionRegistry sessionRegistry,
    IUserRepository users,
    IRelationshipRepository relationships,
    IMailRepository mail,
    ICommandDispatcher commandDispatcher) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.SendPrivateMessage;

    public bool AllowedWhenRestricted => true;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var message = reader.ReadMessage();

        if (player.Silenced) return;

        var target = sessionRegistry.GetByName(message.Recipient);

        if (target is { IsBot: true })
        {
            var reply = await commandDispatcher.DispatchAsync(player, message.Text, null);
            if (reply is not null) player.Enqueue(ServerPacketWriter.SendMessage(target.Name, reply, target.Name, target.Id));
            return;
        }

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