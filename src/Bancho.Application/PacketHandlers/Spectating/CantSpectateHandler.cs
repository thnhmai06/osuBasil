using Bancho.Application.PacketHandlers.Core;
using Bancho.Application.Sessions;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Spectating;

/// <summary>Ported from app/api/domains/cho.py's CantSpectate.</summary>
public sealed class CantSpectateHandler : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.CantSpectate;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var host = player.Spectating;
        if (host is null || player.Stealth) return Task.CompletedTask;

        var packet = ServerPacketWriter.SpectatorCantSpectate(player.Id);
        host.Enqueue(packet);

        foreach (var spectator in host.Spectators) spectator.Enqueue(packet);

        return Task.CompletedTask;
    }
}