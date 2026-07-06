using Bancho.Application.Abstractions;
using Bancho.Application.Commands;
using Bancho.Application.Configuration;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using Microsoft.Extensions.Options;

namespace Bancho.Application.PacketHandlers;

/// <summary>
/// Ported from app/api/domains/cho.py's SendMessage (private). A PM to the bot session routes
/// through the same command dispatcher as public messages (only when command-prefixed — plain
/// chat to the bot and /np previews are not replied to, matching the no-pp scope decision) instead
/// of the mail/away-message path used for real targets.
/// </summary>
public sealed class SendPrivateMessageHandler(
    IPlayerSessionRegistry sessionRegistry,
    IUserRepository users,
    IRelationshipRepository relationships,
    IMailRepository mail,
    ICommandDispatcher commandDispatcher,
    IOptions<ServerBehaviorOptions> serverOptions) : IBanchoPacketHandler
{
    private const string BotName = "BanchoBot";

    public ClientPackets PacketId => ClientPackets.SendPrivateMessage;

    public bool AllowedWhenRestricted => true;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var message = reader.ReadMessage();

        if (player.Silenced)
        {
            return;
        }

        var target = sessionRegistry.GetByName(message.Recipient);

        if (target is not null && target.IsBotClient)
        {
            var prefix = serverOptions.Value.CommandPrefix;
            if (!message.Text.StartsWith(prefix, StringComparison.Ordinal))
            {
                return;
            }

            var result = await commandDispatcher.DispatchAsync(player, message.Text[prefix.Length..], null, target);
            if (result?.Response is { } response)
            {
                player.Enqueue(ServerPacketWriter.SendMessage(BotName, response, player.Name, target.Id));
            }

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
            if (targetUser is null)
            {
                return;
            }

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

            if (target.Status.Action == Domain.Action.Afk && target.AwayMessage is { } awayMessage)
            {
                player.Enqueue(ServerPacketWriter.SendMessage(target.Name, awayMessage, player.Name, target.Id));
            }
        }

        await mail.CreateAsync(player.Id, targetId, message.Text);
    }
}
