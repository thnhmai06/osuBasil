using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.Sessions.Channels;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.PacketHandlers.Channels;

/// <summary>
///     Ported from app/api/domains/cho.py's SendMessage (public channel messages), scoped to plain
///     channels — #spectator/#multiplayer synthetic recipients land in Phase 7. bancho.py has no
///     spam/rate-limit or auto-silence logic on this path (confirmed by reading the source); none is
///     added here either. Bot commands (the "!"-prefixed dispatch layer) are deliberately not wired
///     up yet — a command-prefixed message is broadcast as plain chat like any other message.
/// </summary>
public sealed class SendPublicMessageHandler(IChannelRegistry channelRegistry, IPlayerSessionRegistry sessionRegistry)
    : IBanchoPacketHandler
{
    private const int MaxMessageLength = 2000;

    public ClientPackets PacketId => ClientPackets.SendPublicMessage;

    public bool AllowedWhenRestricted => true;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var message = reader.ReadMessage();

        if (player.Silenced) return Task.CompletedTask;

        var channel = channelRegistry.GetByName(message.Recipient);
        if (channel is null || !channel.Contains(player.Id) || !channel.CanWrite(player.Priv))
            return Task.CompletedTask;

        var text = message.Text.Length > MaxMessageLength ? message.Text[..MaxMessageLength] : message.Text;

        var packet = ServerPacketWriter.SendMessage(player.Name, text, channel.Name, player.Id);
        foreach (var session in sessionRegistry.All)
            if (session.Id != player.Id && channel.Contains(session.Id))
                session.Enqueue(packet);

        return Task.CompletedTask;
    }
}