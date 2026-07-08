using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.UseCases.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

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