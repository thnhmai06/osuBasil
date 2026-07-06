using Bancho.Application.Sessions;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

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
