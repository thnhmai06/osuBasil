using Bancho.Application.Commands;
using Bancho.Application.Configuration;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using Microsoft.Extensions.Options;

namespace Bancho.Application.PacketHandlers;

/// <summary>
/// Ported from app/api/domains/cho.py's SendMessage (public channel messages), scoped to plain
/// channels — #spectator/#multiplayer synthetic recipients land in Phase 7. bancho.py has no
/// spam/rate-limit or auto-silence logic on this path (confirmed by reading the source); none is
/// added here either.
/// </summary>
public sealed class SendPublicMessageHandler(
    IChannelRegistry channelRegistry,
    IPlayerSessionRegistry sessionRegistry,
    ICommandDispatcher commandDispatcher,
    IOptions<ServerBehaviorOptions> serverOptions) : IBanchoPacketHandler
{
    private const int MaxMessageLength = 2000;
    private const string BotName = "BanchoBot";
    private const int BotId = 1;

    public ClientPackets PacketId => ClientPackets.SendPublicMessage;

    public bool AllowedWhenRestricted => true;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var message = reader.ReadMessage();

        if (player.Silenced)
        {
            return;
        }

        var channel = channelRegistry.GetByName(message.Recipient);
        if (channel is null || !channel.Contains(player.Id) || !channel.CanWrite(player.Priv))
        {
            return;
        }

        var text = message.Text.Length > MaxMessageLength ? message.Text[..MaxMessageLength] : message.Text;

        var prefix = serverOptions.Value.CommandPrefix;
        if (text.StartsWith(prefix, StringComparison.Ordinal))
        {
            var result = await commandDispatcher.DispatchAsync(player, text[prefix.Length..], channel, null);
            if (result?.Response is not { } response)
            {
                return;
            }

            var responsePacket = ServerPacketWriter.SendMessage(BotName, response, channel.Name, BotId);
            foreach (var session in sessionRegistry.All)
            {
                if (!channel.Contains(session.Id))
                {
                    continue;
                }

                if (result.Hidden && (session.Priv & Privileges.Staff) == 0)
                {
                    continue;
                }

                session.Enqueue(responsePacket);
            }

            return;
        }

        var packet = ServerPacketWriter.SendMessage(player.Name, text, channel.Name, player.Id);
        foreach (var session in sessionRegistry.All)
        {
            if (session.Id != player.Id && channel.Contains(session.Id))
            {
                session.Enqueue(packet);
            }
        }
    }
}
