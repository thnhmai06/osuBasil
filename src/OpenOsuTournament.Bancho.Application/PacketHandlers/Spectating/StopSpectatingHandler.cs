using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.UseCases.Spectating;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.PacketHandlers.Spectating;

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