using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>Ported from app/api/domains/cho.py's Ping — a no-op.</summary>
public sealed class PingHandler : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.Ping;

    public bool AllowedWhenRestricted => true;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        return Task.CompletedTask;
    }
}