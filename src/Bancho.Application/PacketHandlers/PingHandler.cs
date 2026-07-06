using Bancho.Application.Sessions;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's Ping — a no-op.</summary>
public sealed class PingHandler : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.Ping;

    public bool AllowedWhenRestricted => true;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader) => Task.CompletedTask;
}
