using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.UseCases.Bot;
using Basil.Protocol;
using Basil.Protocol.Packets;
using Action = Basil.Domain.Action;

namespace Basil.Application.PacketHandlers.Channels;

/// <summary>
///     Ported from app/api/domains/cho.py's SendMessage (private). BanchoBot's special-cased routing
///     is re-added (see docs/working-scopes.md) as a command-dispatch shortcut only — DMs to the bot
///     never go through the normal deliver/mail path (nothing ever reads the bot's queue). matchScope
///     is always null for PMs (a private message is never a match's own chat channel, matching
///     bancho.py's ensure_match check), so every `!mp` subcommand is unreachable this way — EXCEPT
///     `!mp make`/`!mp makeprivate`, which are the one pair designed to run with a null match scope
///     (see <see cref="ICommandDispatcher.DispatchAsync" />'s doc comment and
///     <see cref="MpCommandService.MakeAsync" />).
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
            var reply = await commandDispatcher.DispatchAsync(player, message.Text, null, true);
            if (reply is not null)
                // See SendPublicMessageHandler's matching split — a `\n`-embedded reply (e.g. !faq)
                // becomes one chat message per line instead of one message with a visible newline.
                foreach (var line in reply.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    player.Enqueue(ServerPacketWriter.SendMessage(target.Name, line, target.Name, target.Id));

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