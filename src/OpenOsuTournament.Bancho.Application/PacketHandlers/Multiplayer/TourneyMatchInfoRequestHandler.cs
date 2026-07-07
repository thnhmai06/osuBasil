using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;
using OpenOsuTournament.Bancho.Application.UseCases.Multiplayer;
using OpenOsuTournament.Bancho.Domain.Users;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's TourneyMatchInfoRequest.</summary>
public sealed class TourneyMatchInfoRequestHandler(IMatchRegistry matchRegistry) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.TournamentMatchInfoRequest;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var matchId = reader.ReadI32();

        if (matchId is < 0 or >= 64 || (player.Priv & Privileges.Donator) == 0) return Task.CompletedTask;

        var match = matchRegistry.GetById(matchId);
        if (match is null) return Task.CompletedTask;

        player.Enqueue(ServerPacketWriter.UpdateMatch(MatchPacketDataMapper.ToPacketData(match), false));
        return Task.CompletedTask;
    }
}