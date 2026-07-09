using Basil.Protocol.Irc;
using Basil.Protocol.Packets;

namespace Basil.Application.Sessions.Irc;

/// <summary>
///     Default <see cref="IIrcConnection" /> for every bancho <see cref="PlayerSession" /> — re-encodes chat text
///     routed through the IRC core back into a bancho SEND_MESSAGE packet, enqueued for the client's next
///     HTTP poll. Only PRIVMSG has a bancho equivalent; JOIN/PART/QUIT/numerics are IRC-only and ignored here
///     (bancho clients already get channel presence via ChannelInfo, not per-user join/part events).
/// </summary>
public sealed class BanchoIrcBridgeConnection(PlayerSession player) : IIrcConnection
{
    public PlayerSession Player { get; } = player;

    public bool IsExternalIrcClient => false;

    public void Send(IrcMessage message)
    {
        if (message.Command != "PRIVMSG") return;
        if (!IrcMessageWriter.TryParseUserPrefix(message.Prefix, out var senderName, out var senderId)) return;

        Player.Enqueue(ServerPacketWriter.SendMessage(senderName, message.Params[1], message.Params[0], senderId));
    }
}
