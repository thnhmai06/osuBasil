using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.UseCases.Chat;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Channels;

/// <summary>
///     Ported from app/api/domains/cho.py's SendMessage (public channel messages). All routing/
///     broadcast/command-dispatch logic lives in <see cref="ChatDispatchService" /> — shared with
///     private messages and real IRC PRIVMSG, so a bancho client, an IRC client, and BanchoBot all
///     go through the exact same chat core.
/// </summary>
public sealed class SendPublicMessageHandler(ChatDispatchService chatDispatch) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.SendPublicMessage;

    public bool AllowedWhenRestricted => true;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var message = reader.ReadMessage();
        await chatDispatch.SendPrivmsgAsync(player, message.Recipient, message.Text);
    }
}
