using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Spectating;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Spectating;

/// <summary>Ported from app/api/domains/cho.py's StopSpectating.</summary>
public sealed class StopSpectatingHandler(SpectatorService spectatorService) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.StopSpectating;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var host = player.Spectating;
        if (host is not null)
        {
            spectatorService.RemoveSpectator(host, player);
        }

        return Task.CompletedTask;
    }
}
