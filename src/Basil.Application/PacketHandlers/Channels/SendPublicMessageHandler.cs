using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.UseCases.Bot;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Channels;

/// <summary>
///     Ported from app/api/domains/cho.py's SendMessage (public channel messages), scoped to plain
///     channels — #spectator/#multiplayer synthetic recipients land in Phase 7. bancho.py has no
///     spam/rate-limit or auto-silence logic on this path (confirmed by reading the source); none is
///     added here either. After the normal broadcast, a "!"-prefixed message is also handed to
///     ICommandDispatcher; a non-null reply is broadcast to the same channel from the bot's identity
///     (bot doesn't need to be a member of the channel for this — matches bancho.py's channel.send_bot()).
/// </summary>
public sealed class SendPublicMessageHandler(
    IChannelRegistry channelRegistry,
    IPlayerSessionRegistry sessionRegistry,
    ICommandDispatcher commandDispatcher) : IBanchoPacketHandler
{
    private const int MaxMessageLength = 2000;

    public ClientPackets PacketId => ClientPackets.SendPublicMessage;

    public bool AllowedWhenRestricted => true;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var message = reader.ReadMessage();

        if (player.Silenced) return;

        var channel = channelRegistry.GetByName(message.Recipient);
        if (channel is null || !channel.Contains(player.Id) || !channel.CanWrite(player.Priv)) return;

        var text = message.Text.Length > MaxMessageLength ? message.Text[..MaxMessageLength] : message.Text;

        var packet = ServerPacketWriter.SendMessage(player.Name, text, channel.Name, player.Id);
        foreach (var session in sessionRegistry.All)
            if (session.Id != player.Id && channel.Contains(session.Id))
                session.Enqueue(packet);

        var matchScope = player.Match is not null && player.Match.ChatChannelName == channel.Name
            ? player.Match
            : null;
        var reply = await commandDispatcher.DispatchAsync(player, text, matchScope);
        if (reply is null) return;

        var bot = sessionRegistry.GetById(BotBootstrapService.BotId);
        if (bot is null) return;

        // A reply may embed `\n` (e.g. !faq's file contents) — each line becomes its own chat message,
        // matching how a real client displays multiple consecutive lines rather than one with a
        // visible newline. Every other command's reply has no `\n`, so this is a no-op for those.
        foreach (var line in reply.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var replyPacket = ServerPacketWriter.SendMessage(bot.Name, line, channel.Name, bot.Id);
            foreach (var session in sessionRegistry.All)
                if (channel.Contains(session.Id))
                    session.Enqueue(replyPacket);
        }
    }
}