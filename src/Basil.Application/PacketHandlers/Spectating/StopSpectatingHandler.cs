using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.UseCases.Spectating;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Spectating;

/// <summary>Ported from app/api/domains/cho.py's StopSpectating.</summary>
public sealed class StopSpectatingHandler(SpectatorService spectatorService) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.StopSpectating;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var host = player.Spectating;
        if (host is not null) spectatorService.RemoveSpectator(host, player);

        return Task.CompletedTask;
    }
}