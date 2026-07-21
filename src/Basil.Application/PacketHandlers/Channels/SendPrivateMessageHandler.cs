using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Chat;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Channels;

/// <summary>
///     Ported from app/api/domains/cho.py's SendMessage (private). All routing (bot-command shortcut,
///     block/PmPrivate/silence checks) lives in <see cref="ChatDispatchService" /> — shared with public
///     messages and real IRC PRIVMSG.
/// </summary>
public sealed class SendPrivateMessageHandler(ChatDispatchService chatDispatch) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.SendPrivateMessage;

    public bool AllowedWhenRestricted => true;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var message = reader.ReadMessage();
        await chatDispatch.SendPrivmsgAsync(player, message.Recipient, message.Text);
    }
}
