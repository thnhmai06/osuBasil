using Bancho.Application.Sessions;
using Bancho.Protocol;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Core;

/// <summary>Ported from app/api/domains/cho.py's SetAwayMessage.</summary>
public sealed class SetAwayMessageHandler : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.SetAwayMessage;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var message = reader.ReadMessage();
        player.AwayMessage = message.Text;
        return Task.CompletedTask;
    }
}
